using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Flenta.Application.Abstractions.Kyc;
using Flenta.Application.Features.Auth;
using Flenta.Domain.Entities;
using Flenta.Infrastructure.Persistence.Banking;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Flenta.Infrastructure.Kyc;

internal sealed class PostgresKycRepository(BankingDbContext db) : IKycRepository, IKycUnitOfWork
{
    private async Task<T> StorageAsync<T>(Func<Task<T>> action)
    {
        if (string.IsNullOrWhiteSpace(db.Database.GetConnectionString()))
            throw new ApplicationFailureException(FailureKind.Unavailable, "kyc.storage_not_configured", "Identity verification is temporarily unavailable.");
        try { return await action(); }
        catch (Exception exception) when (exception is NpgsqlException or DbUpdateException or TimeoutException)
        { throw new ApplicationFailureException(FailureKind.Unavailable, "kyc.storage_unavailable", "Verification status is temporarily unavailable. Try again later."); }
    }

    public Task<KycVerification?> GetAsync(Guid ownerId, string provider, bool tracked, CancellationToken ct) => StorageAsync(() =>
        (tracked ? db.KycVerifications : db.KycVerifications.AsNoTracking()).Where(v => v.OwnerId == ownerId && v.ProviderName == provider).OrderByDescending(v => v.AttemptNumber).FirstOrDefaultAsync(ct));
    public Task<KycVerification?> FindSessionAsync(string provider, string sessionId, CancellationToken ct) => StorageAsync(() =>
        db.KycVerifications.AsNoTracking().SingleOrDefaultAsync(v => v.ProviderName == provider && v.ProviderSessionId == sessionId, ct));
    public Task<bool> HasReceiptAsync(string digest, CancellationToken ct) => db.KycWebhookReceipts.AnyAsync(r => r.Digest == digest, ct);
    public Task<T> ExecuteAsync<T>(Guid ownerId, Func<CancellationToken, Task<T>> action, CancellationToken ct) => StorageAsync(async () =>
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"kyc:{ownerId:N}"));
        var lockId = BinaryPrimitives.ReadInt64BigEndian(hash);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockId})", ct);
        var result = await action(ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return result;
    });
    public void Add(KycVerification verification) => db.KycVerifications.Add(verification);
    public void Add(KycReviewEvent reviewEvent) => db.KycReviewEvents.Add(reviewEvent);
    public void Add(KycWebhookReceipt receipt) => db.KycWebhookReceipts.Add(receipt);
}
