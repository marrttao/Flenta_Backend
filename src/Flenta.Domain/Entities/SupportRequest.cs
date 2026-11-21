using Flenta.Domain.Exceptions;
namespace Flenta.Domain.Entities;

public sealed class SupportRequest : Entity
{
    private SupportRequest() : base(Guid.NewGuid()) { }
    public SupportRequest(Guid owner, string subject, string kind, DateTimeOffset now) : base(Guid.NewGuid())
    {
        if (owner == Guid.Empty || string.IsNullOrWhiteSpace(subject) || subject.Trim().Length > 120 || kind is not ("Message" or "Callback"))
            throw new DomainValidationException("support.invalid_request", "A subject of up to 120 characters and valid request type are required.");
        OwnerId = owner; Subject = subject.Trim(); Kind = kind; CreatedAt = UpdatedAt = now;
    }
    public Guid OwnerId { get; private set; }
    public string Subject { get; private set; } = "";
    public string Kind { get; private set; } = "Message";
    public string Status { get; private set; } = "Open";
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public void Touch(DateTimeOffset now) { if (Status == "Closed") throw new DomainValidationException("support.closed", "This request is closed."); UpdatedAt = now; }
    public void Close(DateTimeOffset now) { Status = "Closed"; UpdatedAt = now; }
}
public sealed class SupportMessage : Entity
{
    private SupportMessage() : base(Guid.NewGuid()) { }
    public SupportMessage(Guid request, Guid author, bool staff, string body, DateTimeOffset now) : base(Guid.NewGuid())
    {
        if (string.IsNullOrWhiteSpace(body) || body.Trim().Length > 4000)
            throw new DomainValidationException("support.invalid_message", "A message of 1–4000 characters is required.");
        RequestId = request; AuthorId = author; IsStaff = staff; Body = body.Trim(); CreatedAt = now;
    }
    public Guid RequestId { get; private set; }
    public Guid AuthorId { get; private set; }
    public bool IsStaff { get; private set; }
    public string Body { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
}
