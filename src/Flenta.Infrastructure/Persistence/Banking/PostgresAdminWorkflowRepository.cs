using Flenta.Application.Abstractions.Admin;
using Flenta.Application.Features.Admin;
using Flenta.Application.Features.Auth;
using Flenta.Domain.Entities;
using Flenta.Infrastructure.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
namespace Flenta.Infrastructure.Persistence.Banking;

internal sealed class PostgresAdminWorkflowRepository(BankingDbContext db, IOptions<BankingOptions> options, SupabaseAdminDirectory directory, Flenta.Application.Abstractions.Kyc.IIdentityVault identityVault) : IAdminWorkflowRepository
{
    private async Task<T> Read<T>(Func<Task<T>> read)
    {
        if (!options.Value.Enabled) throw new ApplicationFailureException(FailureKind.Unavailable, "admin.storage_not_configured", "Administrative storage is unavailable.");
        try { return await read(); } catch (Exception e) when (e is NpgsqlException or TimeoutException) { throw new ApplicationFailureException(FailureKind.Unavailable, "admin.storage_unavailable", "Administrative storage is temporarily unavailable."); }
    }
    public async Task<IReadOnlyList<Guid>> NotificationRecipientsAsync(Guid? owner, CancellationToken ct)
    {
        var users = await directory.AllAsync(ct);
        if (owner.HasValue && !users.Any(u => u.Id == owner.Value)) throw new ApplicationFailureException(FailureKind.NotFound, "admin.client_not_found", "Client not found.");
        var ids = users.Where(u => !owner.HasValue || u.Id == owner.Value).Select(u => u.Id).ToArray();
        if (ids.Length > 5000) throw new ApplicationFailureException(FailureKind.InvalidRequest, "notification.audience_limit", "Choose a smaller audience; at most 5000 clients per request.");
        return ids;
    }
    public void Add(Notification notification) => db.Notifications.Add(notification);
    public async Task<BankTransaction?> TransactionAsync(Guid id, CancellationToken ct) =>
        (await db.Transactions.FromSqlInterpolated($"SELECT * FROM banking.transactions WHERE \"Id\" = {id} FOR UPDATE").ToListAsync(ct)).SingleOrDefault();
    public async Task<IReadOnlyList<LedgerEntry>> EntriesAsync(Guid id, CancellationToken ct) => await db.Entries.AsNoTracking().Where(e => e.TransactionId == id).ToListAsync(ct);
    public Task<bool> IsCompensatedAsync(Guid id, CancellationToken ct) => db.AdminAudit.AnyAsync(a => a.RelatedTransactionId == id && a.Action == "TransactionCompensation", ct);
    public Task<AdminClientDetail> ClientAsync(Guid owner, CancellationToken ct) => Read(async () =>
    {
        var user = (await directory.AllAsync(ct)).SingleOrDefault(u => u.Id == owner) ?? throw new ApplicationFailureException(FailureKind.NotFound, "admin.client_not_found", "Client not found.");
        var verification = await db.KycVerifications.AsNoTracking().Where(v => v.OwnerId == owner).OrderByDescending(v => v.CreatedAt).ThenByDescending(v => v.AttemptNumber).ThenByDescending(v => v.Id).FirstOrDefaultAsync(ct);
        var manual = verification is null ? null : await db.ManualReviews.AsNoTracking().Where(m => m.VerificationId == verification.Id && m.CreatedAt >= verification.UpdatedAt).OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).FirstOrDefaultAsync(ct);
        var settings = await db.ProfileSettings.AsNoTracking().SingleOrDefaultAsync(s => s.Id == owner, ct);
        var control = await db.ClientControls.AsNoTracking().SingleOrDefaultAsync(s => s.Id == owner, ct);
        var accounts = await db.Accounts.AsNoTracking().Where(a => a.OwnerId == owner && a.Kind == AccountKind.Customer).OrderBy(a => a.CreatedAt).Select(a => new AdminAccount(a.Id, a.CurrencyCode, a.BalanceMinorUnits / 100m, a.Iban, a.Blocked)).ToListAsync(ct);
        var audit = await AuditAsync(owner, ct);
        var snapshot = verification is null ? null : await db.IdentitySnapshots.AsNoTracking().Where(s => s.VerificationId == verification.Id).OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id).FirstOrDefaultAsync(ct);
        var identity = snapshot is null ? null : identityVault.Open(snapshot.VerificationId, snapshot.Ciphertext);
        return new AdminClientDetail(new AdminClient(user.Id, user.DisplayName, user.Email, user.EmailConfirmed, manual is null ? verification?.Status.ToString() ?? "NotStarted" : manual.Approved ? "Verified" : "Rejected", user.CreatedAt),
            settings?.Phone ?? "", control?.Blocked ?? false, control?.CardsFrozen ?? false, accounts, audit.Select(a => new AdminAuditResponse(a.Id, a.ActorId, a.Action, a.Reason, a.TransactionId, a.CreatedAt)).ToArray(), identity);
    });
    public Task<AdminTransactionPage> TransactionsAsync(string query, string? status, int page, DateOnly? date, string? kind, DateTimeOffset now, CancellationToken ct) => Read(async () =>
    {
        var users = await directory.AllAsync(ct); var names = users.ToDictionary(u => u.Id);
        var ids = users.Where(u => u.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) || (u.Email?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)).Select(u => u.Id).ToArray();
        var selected = date ?? DateOnly.FromDateTime(now.UtcDateTime);
        var start = new DateTimeOffset(selected.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)); var end = start.AddDays(1);
        var daily = db.Transactions.AsNoTracking().Where(t => t.CreatedAt >= start && t.CreatedAt < end);
        var dailyTotal = await daily.CountAsync(ct); var completed = await daily.CountAsync(t => t.Status == TransactionStatus.Completed, ct);
        var attention = await daily.CountAsync(t => t.Status != TransactionStatus.Completed, ct);
        var rows = db.Transactions.AsNoTracking();
        if (date.HasValue) rows = rows.Where(t => t.CreatedAt >= start && t.CreatedAt < end);
        if (!string.IsNullOrEmpty(kind)) { var filter = Enum.Parse<TransactionKind>(kind); rows = rows.Where(t => t.Kind == filter); }
        if (!string.IsNullOrEmpty(status)) { var filter = Enum.Parse<TransactionStatus>(status); rows = rows.Where(t => t.Status == filter); }
        if (query.Length > 0) { var isId = Guid.TryParse(query, out var id); rows = rows.Where(t => ids.Contains(t.InitiatorId) || isId && t.Id == id || db.AdminAudit.Any(a => a.TransactionId == t.Id && ids.Contains(a.TargetId) && (a.Action == "BalanceAdjustment" || a.Action == "TransactionCompensation"))); }
        var total = await rows.CountAsync(ct);
        var items = await rows.OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id).Skip((page - 1) * 8).Take(8).ToListAsync(ct);
        var operationIds = items.Select(t => t.Id).ToArray();
        var actors = await db.AdminAudit.AsNoTracking().Where(a => a.TransactionId != null && operationIds.Contains(a.TransactionId.Value) && (a.Action == "BalanceAdjustment" || a.Action == "TransactionCompensation")).Select(a => new { a.TransactionId, a.TargetId }).ToDictionaryAsync(a => a.TransactionId!.Value, a => a.TargetId, ct);
        return new AdminTransactionPage(items.Select(t =>
        {
            var owner = actors.GetValueOrDefault(t.Id, t.InitiatorId);
            return new AdminTransaction(t.Id, owner, names.GetValueOrDefault(owner)?.DisplayName ?? "Client", names.GetValueOrDefault(owner)?.Email, t.Kind.ToString(), t.AmountMinorUnits / 100m, t.CurrencyCode, t.Status.ToString(), t.CreatedAt);
        }).ToArray(), total, page, selected, dailyTotal, completed, attention);
    });
    public Task<AdminKycPage> KycAsync(string? status, int page, string query, DateTimeOffset now, CancellationToken ct) => Read(async () =>
    {
        var users = (await directory.AllAsync(ct)).ToDictionary(u => u.Id); var owners = users.Keys.ToArray();
        var rows = db.KycVerifications.AsNoTracking().Where(v => owners.Contains(v.OwnerId) && !db.KycVerifications.Any(n => n.OwnerId == v.OwnerId && (n.CreatedAt > v.CreatedAt || n.CreatedAt == v.CreatedAt && n.AttemptNumber > v.AttemptNumber)));
        var day = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var awaiting = await rows.CountAsync(v => (v.Status == KycStatus.InProgress || v.Status == KycStatus.PendingReview || v.Status == KycStatus.NeedsResubmission) && !db.ManualReviews.Any(m => m.VerificationId == v.Id && m.CreatedAt >= v.UpdatedAt), ct);
        var inReview = await rows.CountAsync(v => v.Status == KycStatus.PendingReview && !db.ManualReviews.Any(m => m.VerificationId == v.Id && m.CreatedAt >= v.UpdatedAt), ct);
        var approvedToday = await rows.CountAsync(v => db.ManualReviews.Where(m => m.VerificationId == v.Id && m.CreatedAt >= v.UpdatedAt).OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).Select(m => m.Approved && m.CreatedAt >= day).FirstOrDefault()
            || v.Status == KycStatus.Verified && v.VerifiedAt >= day && !db.ManualReviews.Any(m => m.VerificationId == v.Id && m.CreatedAt >= v.UpdatedAt), ct);
        if (query.Length > 0)
        {
            var isId = Guid.TryParse(query, out var id);
            var matches = users.Values.Where(u => u.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) || (u.Email?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)).Select(u => u.Id).ToArray();
            string? tax = null;
            try { tax = identityVault.TaxFingerprint(query); } catch (Flenta.Domain.Exceptions.DomainValidationException) { }
            rows = rows.Where(v => matches.Contains(v.OwnerId) || isId && (v.Id == id || v.OwnerId == id)
                || tax != null && db.IdentitySnapshots.Where(s => s.VerificationId == v.Id).OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id).Select(s => s.TaxFingerprint).FirstOrDefault() == tax);
        }
        if (!string.IsNullOrEmpty(status)) { var filter = Enum.Parse<KycStatus>(status); rows = rows.Where(v => v.Status == filter); }
        var total = await rows.CountAsync(ct);
        var items = await rows.OrderByDescending(v => v.CreatedAt).ThenByDescending(v => v.Id).Skip((page - 1) * 8).Take(8).ToListAsync(ct);
        var result = new List<AdminKycItem>();
        foreach (var v in items)
        {
            var manual = await db.ManualReviews.AsNoTracking().Where(m => m.VerificationId == v.Id && m.CreatedAt >= v.UpdatedAt).OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).Select(m => (bool?)m.Approved).FirstOrDefaultAsync(ct);
            var snapshot = await db.IdentitySnapshots.AsNoTracking().Where(s => s.VerificationId == v.Id).OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id).FirstOrDefaultAsync(ct);
            var identity = snapshot is null ? null : identityVault.Open(v.Id, snapshot.Ciphertext);
            result.Add(new(v.Id, v.OwnerId, identity?.FullName ?? users[v.OwnerId].DisplayName, users[v.OwnerId].Email, v.Status.ToString(), v.ProviderName, v.AttemptNumber, v.Sandbox, v.CreatedAt, manual, identity?.TaxNumber));
        }
        return new AdminKycPage(result, total, page, awaiting, inReview, approvedToday);
    });
    public async Task<KycVerification?> VerificationAsync(Guid id, CancellationToken ct)
    {
        var known = await Read(() => db.KycVerifications.AsNoTracking().SingleOrDefaultAsync(v => v.Id == id, ct));
        if (known is null || db.Database.CurrentTransaction is null) return known;
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"kyc:{known.OwnerId:N}"));
        var lockId = System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(hash);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockId})", ct);
        return (await db.KycVerifications.FromSqlInterpolated($"SELECT * FROM kyc.verifications WHERE \"Id\" = {id} FOR UPDATE").ToListAsync(ct)).SingleOrDefault();
    }
    public Task<bool> IsLatestVerificationAsync(KycVerification v, CancellationToken ct) => db.KycVerifications.Where(n => n.OwnerId == v.OwnerId && n.ProviderName == v.ProviderName).OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.AttemptNumber).ThenByDescending(n => n.Id).Select(n => n.Id == v.Id).FirstAsync(ct);
    public async Task<ClientControl?> ControlAsync(Guid owner, bool forUpdate, CancellationToken ct)
    {
        if (forUpdate) await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({'C' + owner.ToString("N")}, 0))", ct);
        return await db.ClientControls.SingleOrDefaultAsync(c => c.Id == owner, ct);
    }
    public Task<AdminAudit?> AuditByKeyAsync(Guid actor, Guid key, CancellationToken ct) => db.AdminAudit.AsNoTracking().SingleOrDefaultAsync(a => a.ActorId == actor && a.IdempotencyKey == key, ct);
    public async Task<IReadOnlyList<AdminAudit>> AuditAsync(Guid owner, CancellationToken ct) => await db.AdminAudit.AsNoTracking().Where(a => a.TargetId == owner).OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id).Take(50).ToListAsync(ct);
    public async Task<IReadOnlyList<BankAccount>> ClientAccountsAsync(Guid owner, CancellationToken ct) => await db.Accounts.AsNoTracking().Where(a => a.OwnerId == owner).OrderBy(a => a.Id).ToListAsync(ct);
    public async Task<IReadOnlyList<VirtualCard>> ClientCardsAsync(Guid owner, CancellationToken ct) => await db.Cards.FromSqlInterpolated($"SELECT c.* FROM banking.cards c JOIN banking.accounts a ON a.\"Id\" = c.\"AccountId\" WHERE a.\"OwnerId\" = {owner} ORDER BY c.\"Id\" FOR UPDATE OF c").ToListAsync(ct);
    public void Add(AdminAudit audit) => db.AdminAudit.Add(audit);
    public void Add(ClientControl control) => db.ClientControls.Add(control);
    public void Add(ManualKycReview review) => db.ManualReviews.Add(review);
}
