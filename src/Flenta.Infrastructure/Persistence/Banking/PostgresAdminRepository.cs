using Flenta.Application.Abstractions.Admin;
using Flenta.Application.Features.Admin;
using Flenta.Application.Features.Auth;
using Flenta.Domain.Entities;
using Flenta.Infrastructure.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Flenta.Infrastructure.Persistence.Banking;

internal sealed class PostgresAdminRepository(BankingDbContext db, IOptions<BankingOptions> options,
    SupabaseAdminDirectory directory) : IAdminRepository
{
    public Task<AdminOverviewResponse> OverviewAsync(DateTimeOffset now, CancellationToken ct) => ReadAsync(async () =>
    {
        var date = DateOnly.FromDateTime(now.UtcDateTime);
        var start = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var users = await directory.AllAsync(ct);
        var names = users.ToDictionary(u => u.Id);
        var clientIds = users.Select(u => u.Id).ToArray();
        var latest = db.KycVerifications.AsNoTracking().Where(v => clientIds.Contains(v.OwnerId)
            && !db.KycVerifications.Any(n => n.OwnerId == v.OwnerId && (n.CreatedAt > v.CreatedAt || n.CreatedAt == v.CreatedAt && n.AttemptNumber > v.AttemptNumber)));
        var verified = await latest.CountAsync(v =>
            (db.ManualReviews.Where(m => m.VerificationId == v.Id && m.CreatedAt >= v.UpdatedAt).OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
                .Select(m => (bool?)m.Approved).FirstOrDefault() ?? v.Status == KycStatus.Verified), ct);
        var today = await db.Transactions.AsNoTracking().CountAsync(t => t.CreatedAt >= start && t.CreatedAt < start.AddDays(1), ct);
        var recent = await db.Transactions.AsNoTracking().OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)
            .Take(7).Select(t => new { t.Id, t.InitiatorId, ClientId = db.AdminAudit.Where(a => a.TransactionId == t.Id && (a.Action == "BalanceAdjustment" || a.Action == "TransactionCompensation")).Select(a => (Guid?)a.TargetId).FirstOrDefault(), t.Kind, t.AmountMinorUnits, t.CurrencyCode, t.Status, t.CreatedAt }).ToListAsync(ct);
        var transactions = recent.Select(t => new AdminTransaction(t.Id, t.ClientId ?? t.InitiatorId,
            names.GetValueOrDefault(t.ClientId ?? t.InitiatorId)?.DisplayName ?? $"Client {t.InitiatorId.ToString("N")[..8]}",
            names.GetValueOrDefault(t.ClientId ?? t.InitiatorId)?.Email, t.Kind.ToString(), t.AmountMinorUnits / 100m, t.CurrencyCode, t.Status.ToString(), t.CreatedAt)).ToArray();
        return new AdminOverviewResponse(users.Count, verified, today, date, now, transactions);
    });

    public Task<AdminClientsResponse> ClientsAsync(string email, int page, int pageSize, CancellationToken ct) => ReadAsync(async () =>
    {
        var clients = await directory.ListAsync(page, pageSize, email, ct);
        var totalClients = email.Length == 0 ? clients.Total : (await directory.ListAsync(1, 1, "", ct)).Total;
        var ids = clients.Users.Select(u => u.Id).ToArray();
        var verifications = await db.KycVerifications.AsNoTracking().Where(v => ids.Contains(v.OwnerId))
            .OrderByDescending(v => v.CreatedAt).ThenByDescending(v => v.AttemptNumber).ThenByDescending(v => v.Id)
            .Select(v => new { v.Id, v.OwnerId, v.Status, v.UpdatedAt }).ToListAsync(ct);
        var latest = verifications.GroupBy(v => v.OwnerId).Select(g => g.First()).ToArray();
        var verificationIds = latest.Select(v => v.Id).ToArray();
        var reviews = await db.ManualReviews.AsNoTracking().Where(m => verificationIds.Contains(m.VerificationId)).OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).ToListAsync(ct);
        var statuses = latest.ToDictionary(v => v.OwnerId, v =>
        {
            var decision = reviews.FirstOrDefault(m => m.VerificationId == v.Id && m.CreatedAt >= v.UpdatedAt);
            return decision is null ? v.Status.ToString() : decision.Approved ? "Verified" : "Rejected";
        });
        return new AdminClientsResponse(clients.Users.Select(u => new AdminClient(u.Id, u.DisplayName, u.Email, u.EmailConfirmed,
            statuses.GetValueOrDefault(u.Id, "NotStarted"), u.CreatedAt)).ToArray(), clients.Total, totalClients, page, pageSize);
    });

    private async Task<T> ReadAsync<T>(Func<Task<T>> query)
    {
        if (!options.Value.Enabled)
            throw new ApplicationFailureException(FailureKind.Unavailable, "admin.storage_not_configured", "Administrative data is unavailable. Contact the administrator.");
        try { return await query(); }
        catch (Exception failure) when (failure is NpgsqlException or TimeoutException)
        {
            throw new ApplicationFailureException(FailureKind.Unavailable, "admin.storage_unavailable", "Administrative data is temporarily unavailable. Try again later.");
        }
    }
}
