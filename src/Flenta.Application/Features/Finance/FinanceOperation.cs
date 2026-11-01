using System.Security.Cryptography;
using System.Text;
using Flenta.Application.Abstractions.Banking;
using Flenta.Application.Features.Auth;
using Flenta.Application.Features.Transactions;
using Flenta.Domain.Entities;
using Flenta.Domain.Exceptions;

namespace Flenta.Application.Features.Finance;

internal static class FinanceOperation
{
    public static string Fingerprint(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static async Task<TransactionResponse?> ExistingAsync(IBankingRepository repository, Guid owner, Guid key, string fingerprint, CancellationToken ct)
    {
        var existing = await repository.ByKeyAsync(owner, key, ct);
        if (existing is null) return null;
        if (existing.RequestFingerprint != fingerprint)
            throw new ApplicationFailureException(FailureKind.Conflict, "transaction.key_reused", "This idempotency key was used with another request.");
        return TransactionService.ToResponse(existing);
    }
    public static void RequireKey(Guid key)
    {
        if (key == Guid.Empty) throw new DomainValidationException("transaction.idempotency_key_required", "A non-empty Idempotency-Key UUID header is required.");
    }
}
