using Flenta.Domain.Entities;
using Microsoft.EntityFrameworkCore;
namespace Flenta.Infrastructure.Persistence.Banking;

internal sealed partial class PostgresBankingStore
{
    private async Task AddTransactionNotificationsAsync(CancellationToken ct)
    {
        var completed = db.ChangeTracker.Entries<BankTransaction>().Where(e => e.State == EntityState.Added && e.Entity.Status == TransactionStatus.Completed).Select(e => e.Entity).ToArray();
        foreach (var transaction in completed)
        {
            var owners = await db.Accounts.Where(a => a.Id == transaction.SourceAccountId || a.Id == transaction.DestinationAccountId).Where(a => a.OwnerId != null).Select(a => a.OwnerId!.Value).Distinct().ToListAsync(ct);
            foreach (var owner in owners)
            {
                if (await db.ProfileSettings.AnyAsync(s => s.Id == owner && !s.TransactionNotifications, ct)) continue;
                db.Notifications.Add(new Notification(owner, "Transaction completed",
                    $"{transaction.Kind}: {transaction.AmountMinorUnits / 100m:0.00} {transaction.CurrencyCode}. Reference {transaction.Id:D}.",
                    transaction.FinishedAt ?? transaction.CreatedAt));
            }
        }
    }
}
