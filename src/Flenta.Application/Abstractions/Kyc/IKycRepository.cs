using Flenta.Domain.Entities;

namespace Flenta.Application.Abstractions.Kyc;

public interface IKycRepository
{
    Task<KycVerification?> GetAsync(Guid ownerId, string provider, bool tracked, CancellationToken cancellationToken);
    Task<KycVerification?> FindSessionAsync(string provider, string sessionId, CancellationToken cancellationToken);
    Task<bool> HasReceiptAsync(string digest, CancellationToken cancellationToken);
    Task<bool?> ManualDecisionAsync(KycVerification verification, CancellationToken cancellationToken) => Task.FromResult<bool?>(null);
    Task<KycIdentitySnapshot?> LatestIdentityAsync(Guid verificationId, CancellationToken cancellationToken) => Task.FromResult<KycIdentitySnapshot?>(null);
    void Add(KycIdentitySnapshot snapshot);
    void Add(KycVerification verification);
    void Add(KycReviewEvent reviewEvent);
    void Add(KycWebhookReceipt receipt);
}

public interface IKycUnitOfWork
{
    Task<T> ExecuteAsync<T>(Guid ownerId, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken);
}

public interface IKycAccessPolicy
{
    Task RequireVerifiedAsync(CancellationToken cancellationToken);
}
