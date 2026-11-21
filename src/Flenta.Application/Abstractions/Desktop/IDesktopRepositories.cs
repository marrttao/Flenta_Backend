using Flenta.Domain.Entities;
namespace Flenta.Application.Abstractions.Desktop;

public interface ISettingsRepository
{
    Task<ProfileSettings?> SettingsAsync(Guid owner, CancellationToken ct);
    void Add(ProfileSettings settings);
}
public interface IRecipientRepository
{
    Task<IReadOnlyList<SavedRecipient>> RecipientsAsync(Guid owner, CancellationToken ct);
    Task<SavedRecipient?> RecipientAsync(Guid owner, Guid id, CancellationToken ct);
    void Add(SavedRecipient recipient);
    void Remove(SavedRecipient recipient);
}
public interface ISupportRepository
{
    Task<IReadOnlyList<SupportRequest>> RequestsAsync(Guid owner, bool staff, CancellationToken ct);
    Task<SupportRequest?> RequestAsync(Guid owner, Guid id, bool staff, CancellationToken ct);
    Task<IReadOnlyList<SupportMessage>> MessagesAsync(Guid request, CancellationToken ct);
    void Add(SupportRequest request);
    void Add(SupportMessage message);
}
public interface INotificationRepository
{
    Task<IReadOnlyList<Notification>> NotificationsAsync(Guid owner, CancellationToken ct);
    Task<IReadOnlyList<NotificationReceipt>> ReceiptsAsync(Guid owner, CancellationToken ct);
    void Add(NotificationReceipt receipt);
}
