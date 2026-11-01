using Flenta.Domain.Exceptions;

namespace Flenta.Domain.Entities;

public enum KycStatus { NotStarted, InProgress, PendingReview, NeedsResubmission, Rejected, Verified, Expired }

public sealed class KycVerification : Entity
{
    private KycVerification() : base(Guid.NewGuid()) { }
    public KycVerification(Guid ownerId, string configurationId, bool sandbox, DateTimeOffset now, string providerName = "Didit", int attemptNumber = 1) : base(Guid.NewGuid())
    {
        if (ownerId == Guid.Empty || string.IsNullOrWhiteSpace(configurationId) || configurationId.Length > 100
            || string.IsNullOrWhiteSpace(providerName) || providerName.Length > 32 || attemptNumber < 1)
            throw new DomainValidationException("kyc.invalid_configuration", "Invalid verification configuration.");
        OwnerId = ownerId; ExternalUserId = ExternalId(ownerId); ConfigurationId = configurationId;
        ProviderName = providerName; AttemptNumber = attemptNumber;
        Sandbox = sandbox; CreatedAt = now; UpdatedAt = now;
    }
    public Guid OwnerId { get; private set; }
    public string ExternalUserId { get; private set; } = "";
    public string ProviderName { get; private set; } = "";
    public string ConfigurationId { get; private set; } = "";
    public int AttemptNumber { get; private set; }
    public bool Sandbox { get; private set; }
    public string? ProviderSessionId { get; private set; }
    public string? DecisionReference { get; private set; }
    public KycStatus Status { get; private set; }
    public string? FailureCode { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? VerifiedAt { get; private set; }

    public static string ExternalId(Guid owner) => $"flenta-{owner:N}";
    public bool Matches(string provider, string configuration, bool sandbox) => ProviderName == provider && ConfigurationId == configuration && Sandbox == sandbox;
    public bool CanAccess(string provider, string configuration, bool sandbox) => Status == KycStatus.Verified && Matches(provider, configuration, sandbox);
    public KycReviewEvent? Start(DateTimeOffset now)
    {
        if (Status is KycStatus.Verified or KycStatus.Rejected or KycStatus.Expired)
            throw new DomainValidationException("kyc.cannot_restart", "This verification cannot be restarted.");
        return Status == KycStatus.NotStarted ? Change(KycStatus.InProgress, null, null, now) : null;
    }

    public KycReviewEvent? ApplyProviderResult(string sessionId, string? decisionReference, KycStatus status,
        bool requirementsMet, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || sessionId.Length > 100 || decisionReference?.Length > 100
            || ProviderSessionId is not null && ProviderSessionId != sessionId || !Enum.IsDefined(status) || status == KycStatus.NotStarted)
            throw new DomainValidationException("kyc.provider_mismatch", "Verification reference does not match.");
        ProviderSessionId = sessionId;
        var failure = status switch
        {
            KycStatus.Rejected => "kyc.rejected",
            KycStatus.NeedsResubmission => "kyc.resubmission_required",
            KycStatus.Expired => "kyc.session_expired",
            _ => null
        };
        if (status == KycStatus.Verified && !requirementsMet)
        { status = KycStatus.NeedsResubmission; failure = "kyc.requirements_not_met"; }
        return Change(status, decisionReference, failure, now);
    }

    private KycReviewEvent? Change(KycStatus status, string? reviewId, string? failure, DateTimeOffset now)
    {
        if (Status == status && FailureCode == failure) { DecisionReference = reviewId; return null; }
        var previous = Status;
        Status = status; DecisionReference = reviewId; FailureCode = failure; UpdatedAt = now;
        VerifiedAt = status == KycStatus.Verified ? VerifiedAt ?? now : null;
        return new KycReviewEvent(Id, OwnerId, previous, status, failure, now);
    }
}

public sealed class KycReviewEvent : Entity
{
    private KycReviewEvent() : base(Guid.NewGuid()) { }
    public KycReviewEvent(Guid verificationId, Guid ownerId, KycStatus previous, KycStatus current, string? failure, DateTimeOffset now) : base(Guid.NewGuid())
    { VerificationId = verificationId; OwnerId = ownerId; PreviousStatus = previous; Status = current; FailureCode = failure; CreatedAt = now; }
    public Guid VerificationId { get; private set; }
    public Guid OwnerId { get; private set; }
    public KycStatus PreviousStatus { get; private set; }
    public KycStatus Status { get; private set; }
    public string? FailureCode { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}

public sealed class KycWebhookReceipt
{
    private KycWebhookReceipt() { }
    public KycWebhookReceipt(string digest, Guid ownerId, DateTimeOffset now) { Digest = digest; OwnerId = ownerId; ReceivedAt = now; }
    public string Digest { get; private set; } = "";
    public Guid OwnerId { get; private set; }
    public DateTimeOffset ReceivedAt { get; private set; }
}
