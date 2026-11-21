using Flenta.Domain.Exceptions;
namespace Flenta.Domain.Entities;

public sealed class AdminAudit : Entity
{
    private AdminAudit() : base(Guid.NewGuid()) { }
    public AdminAudit(Guid actor, Guid target, Guid key, string action, string reason, string fingerprint, DateTimeOffset now, Guid? transactionId = null, Guid? relatedTransactionId = null) : base(Guid.NewGuid())
    {
        if (actor == Guid.Empty || target == Guid.Empty || key == Guid.Empty || string.IsNullOrWhiteSpace(reason) || reason.Trim().Length is < 5 or > 1000)
            throw new DomainValidationException("admin.reason_required", "An audit reason of 5–1000 characters is required.");
        ActorId = actor; TargetId = target; IdempotencyKey = key; Action = action; Reason = reason.Trim(); Fingerprint = fingerprint; CreatedAt = now; TransactionId = transactionId; RelatedTransactionId = relatedTransactionId;
    }
    public Guid ActorId { get; private set; }
    public Guid TargetId { get; private set; }
    public Guid IdempotencyKey { get; private set; }
    public string Action { get; private set; } = "";
    public string Reason { get; private set; } = "";
    public string Fingerprint { get; private set; } = "";
    public Guid? TransactionId { get; private set; }
    public Guid? RelatedTransactionId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
public sealed class ClientControl : Entity
{
    private ClientControl() : base(Guid.NewGuid()) { }
    public ClientControl(Guid owner) : base(owner) { if (owner == Guid.Empty) throw new ArgumentException("Owner is required."); }
    public bool Blocked { get; private set; }
    public bool CardsFrozen { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public void Update(bool blocked, bool frozen, DateTimeOffset now) { Blocked = blocked; CardsFrozen = frozen; UpdatedAt = now; }
}
public sealed class ManualKycReview : Entity
{
    private ManualKycReview() : base(Guid.NewGuid()) { }
    public ManualKycReview(Guid verification, Guid actor, Guid audit, bool approved, DateTimeOffset now) : base(Guid.NewGuid())
    { VerificationId = verification; ActorId = actor; AuditId = audit; Approved = approved; CreatedAt = now; }
    public Guid VerificationId { get; private set; }
    public Guid ActorId { get; private set; }
    public Guid AuditId { get; private set; }
    public bool Approved { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
