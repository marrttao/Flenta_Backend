using Flenta.Application.Abstractions.Desktop;
using Flenta.Application.Features.Auth;
using Flenta.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
namespace Flenta.Infrastructure.Persistence.Banking;

internal sealed class PostgresDesktopRepository(BankingDbContext db, IOptions<BankingOptions> options) :
    ISettingsRepository, IRecipientRepository, ISupportRepository, INotificationRepository
{
    private async Task<T> Read<T>(Func<Task<T>> read)
    {
        if (!options.Value.Enabled) throw new ApplicationFailureException(FailureKind.Unavailable, "banking.not_configured", "Banking service is unavailable.");
        try { return await read(); }
        catch (Exception e) when (e is NpgsqlException or TimeoutException)
        {
            throw new ApplicationFailureException(FailureKind.Unavailable, "banking.storage_unavailable", "Storage is unavailable. Try again later.");
        }
    }
    public Task<ProfileSettings?> SettingsAsync(Guid owner, CancellationToken ct) => Read(() => db.ProfileSettings.SingleOrDefaultAsync(s => s.Id == owner, ct));
    public async Task<IReadOnlyList<SavedRecipient>> RecipientsAsync(Guid owner, CancellationToken ct) => await Read(() => db.SavedRecipients.Where(r => r.OwnerId == owner).OrderBy(r => r.Name).ThenBy(r => r.Id).Take(100).ToListAsync(ct));
    public Task<SavedRecipient?> RecipientAsync(Guid owner, Guid id, CancellationToken ct) => Read(() => db.SavedRecipients.SingleOrDefaultAsync(r => r.Id == id && r.OwnerId == owner, ct));
    public async Task<IReadOnlyList<SupportRequest>> RequestsAsync(Guid owner, bool staff, CancellationToken ct) => await Read(() => db.SupportRequests.AsNoTracking().Where(r => staff || r.OwnerId == owner).OrderBy(r => r.Status == "Closed").ThenByDescending(r => r.UpdatedAt).ThenByDescending(r => r.Id).Take(100).ToListAsync(ct));
    public async Task<SupportRequest?> RequestAsync(Guid owner, Guid id, bool staff, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null)
            return (await db.SupportRequests.FromSqlInterpolated($"SELECT * FROM banking.support_requests WHERE \"Id\" = {id} AND ({staff} OR \"OwnerId\" = {owner}) FOR UPDATE").ToListAsync(ct)).SingleOrDefault();
        return await Read(() => db.SupportRequests.SingleOrDefaultAsync(r => r.Id == id && (staff || r.OwnerId == owner), ct));
    }
    public async Task<IReadOnlyList<SupportMessage>> MessagesAsync(Guid request, CancellationToken ct) => await Read(() => db.SupportMessages.AsNoTracking().Where(m => m.RequestId == request).OrderBy(m => m.CreatedAt).ThenBy(m => m.Id).Take(1000).ToListAsync(ct));
    public async Task<IReadOnlyList<Notification>> NotificationsAsync(Guid owner, CancellationToken ct) => await Read(() => db.Notifications.AsNoTracking().Where(n => n.OwnerId == owner).OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id).Take(100).ToListAsync(ct));
    public async Task<IReadOnlyList<NotificationReceipt>> ReceiptsAsync(Guid owner, CancellationToken ct) => await Read(() => db.NotificationReceipts.AsNoTracking().Where(n => n.OwnerId == owner && db.Notifications.Where(v => v.OwnerId == owner).OrderByDescending(v => v.CreatedAt).ThenByDescending(v => v.Id).Take(100).Select(v => v.Id).Contains(n.NotificationId)).ToListAsync(ct));
    public void Add(ProfileSettings s) => db.ProfileSettings.Add(s);
    public void Add(SavedRecipient s) => db.SavedRecipients.Add(s);
    public void Remove(SavedRecipient s) => db.SavedRecipients.Remove(s);
    public void Add(SupportRequest s) => db.SupportRequests.Add(s);
    public void Add(SupportMessage s) => db.SupportMessages.Add(s);
    public void Add(NotificationReceipt s) => db.NotificationReceipts.Add(s);
}
