using Flenta.Domain.Exceptions;
namespace Flenta.Domain.Entities;

public sealed class Notification : Entity
{
    private Notification() : base(Guid.NewGuid()) { }
    public Notification(Guid owner, string title, string body, DateTimeOffset now) : base(Guid.NewGuid())
    {
        if (owner == Guid.Empty || string.IsNullOrWhiteSpace(title) || title.Length > 120 || body.Length > 2000)
            throw new DomainValidationException("notification.invalid", "Notification content is invalid.");
        OwnerId = owner; Title = title; Body = body; CreatedAt = now;
    }
    public Guid OwnerId { get; private set; }
    public string Title { get; private set; } = "";
    public string Body { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
}
public sealed class NotificationReceipt : Entity
{
    private NotificationReceipt() : base(Guid.NewGuid()) { }
    public NotificationReceipt(Guid owner, Guid notification, DateTimeOffset now) : base(Guid.NewGuid()) { OwnerId = owner; NotificationId = notification; ReadAt = now; }
    public Guid OwnerId { get; private set; }
    public Guid NotificationId { get; private set; }
    public DateTimeOffset ReadAt { get; private set; }
}
