using Flenta.Application.Abstractions;
using Flenta.Application.Abstractions.Banking;
using Flenta.Application.Abstractions.Desktop;
using Flenta.Application.Features.Accounts;
using Flenta.Domain.Entities;
namespace Flenta.Application.Features.Notifications;

public sealed record NotificationResponse(Guid Id, string Title, string Body, DateTimeOffset CreatedAt, bool IsRead);
public sealed class NotificationService(INotificationRepository repository, IBankingUnitOfWork work, ICurrentUser user, IClock clock)
{
    public async Task<IReadOnlyList<NotificationResponse>> ListAsync(CancellationToken ct)
    {
        var receipts = (await repository.ReceiptsAsync(user.Profile.Id, ct)).Select(r => r.NotificationId).ToHashSet();
        return (await repository.NotificationsAsync(user.Profile.Id, ct)).Select(n => new NotificationResponse(n.Id, n.Title, n.Body, n.CreatedAt, receipts.Contains(n.Id))).ToArray();
    }
    public Task<bool> ReadAsync(Guid id, CancellationToken ct) =>
        work.ExecuteAsync(user.Profile.Id, id, async token =>
        {
            if (!(await repository.NotificationsAsync(user.Profile.Id, token)).Any(n => n.Id == id)) throw AccountService.Missing();
            if (!(await repository.ReceiptsAsync(user.Profile.Id, token)).Any(r => r.NotificationId == id)) repository.Add(new NotificationReceipt(user.Profile.Id, id, clock.UtcNow));
            return true;
        }, ct);
}
