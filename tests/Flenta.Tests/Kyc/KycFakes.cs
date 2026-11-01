using Flenta.Application.Abstractions;
using Flenta.Application.Abstractions.Kyc;
using Flenta.Application.Features.Auth;
using Flenta.Domain.Entities;
using Flenta.Domain.ValueObjects;

namespace Flenta.Tests.Kyc;

internal sealed class KycMemoryStore : IKycRepository, IKycUnitOfWork
{
    public List<KycVerification> Verifications { get; } = [];
    public List<KycReviewEvent> Reviews { get; } = [];
    public List<KycWebhookReceipt> Receipts { get; } = [];
    private readonly SemaphoreSlim gate = new(1, 1);
    public KycVerification? Latest(Guid owner, string provider = "Didit") => Verifications.Where(v => v.OwnerId == owner && v.ProviderName == provider).MaxBy(v => v.AttemptNumber);
    public Task<KycVerification?> GetAsync(Guid ownerId, string provider, bool tracked, CancellationToken ct) => Task.FromResult(Latest(ownerId, provider));
    public Task<KycVerification?> FindSessionAsync(string provider, string sessionId, CancellationToken ct) => Task.FromResult(Verifications.SingleOrDefault(v => v.ProviderName == provider && v.ProviderSessionId == sessionId));
    public Task<bool> HasReceiptAsync(string digest, CancellationToken ct) => Task.FromResult(Receipts.Any(r => r.Digest == digest));
    public void Add(KycVerification value) => Verifications.Add(value);
    public void Add(KycReviewEvent value) => Reviews.Add(value);
    public void Add(KycWebhookReceipt value) => Receipts.Add(value);
    public async Task<T> ExecuteAsync<T>(Guid ownerId, Func<CancellationToken, Task<T>> action, CancellationToken ct)
    { await gate.WaitAsync(ct); try { return await action(ct); } finally { gate.Release(); } }
}

internal sealed class KycTestProvider : IKycProvider
{
    public const string WorkflowId = "bc000000-0000-0000-0000-000000000001";
    public const string SessionId = "ab000000-0000-0000-0000-000000000001";
    public KycProviderConfiguration Configuration { get; set; } = new("Didit", WorkflowId, true);
    public KycStatus Status { get; set; } = KycStatus.PendingReview;
    public bool RequirementsMet { get; set; } = true;
    public bool Offline { get; set; }
    public string? LastExternalId { get; private set; }
    public int Requests { get; private set; }
    public int Created { get; private set; }
    private readonly Dictionary<string, string> owners = [];
    public Task<KycSessionSnapshot> CreateSessionAsync(string externalUserId, CancellationToken ct)
    {
        LastExternalId = externalUserId; Created++;
        var id = Created == 1 ? SessionId : Guid.NewGuid().ToString("D");
        owners[id] = externalUserId;
        return Task.FromResult(Snapshot(id, externalUserId, KycStatus.InProgress));
    }
    public Task<KycSessionSnapshot> GetSessionAsync(string sessionId, CancellationToken ct)
    {
        Requests++;
        if (Offline) throw new ApplicationFailureException(FailureKind.Unavailable, "kyc.provider_unavailable", "Temporarily unavailable.");
        return Task.FromResult(Snapshot(sessionId, owners[sessionId], Status));
    }
    private KycSessionSnapshot Snapshot(string id, string owner, KycStatus status) => new(id, owner, Configuration.ConfigurationId,
        Configuration.Sandbox, status, RequirementsMet, "https://verify.didit.me/session/fixture-" + id);
}

internal sealed class KycProfiles : IUserProfileRepository
{
    public static readonly Guid Alice = Guid.Parse("10000000-0000-0000-0000-000000000001");
    public static readonly Guid Bob = Guid.Parse("10000000-0000-0000-0000-000000000002");
    public Task<UserProfile> GetCurrentAsync(string accessToken, CancellationToken ct) => Task.FromResult(new UserProfile(
        accessToken == "bob" ? Bob : Alice, EmailAddress.Create(accessToken + "@example.com"), accessToken, accessToken != "unconfirmed"));
    public Task<UserProfile> UpdateCurrentAsync(string accessToken, string displayName, CancellationToken ct) => GetCurrentAsync(accessToken, ct);
}
