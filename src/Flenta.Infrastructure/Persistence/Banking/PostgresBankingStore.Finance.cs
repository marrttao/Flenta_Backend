using System.Data;
using Flenta.Application.Features.Finance;
using Flenta.Domain.Entities;
using Flenta.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Flenta.Infrastructure.Persistence.Banking;

internal sealed partial class PostgresBankingStore
{
    public async Task<IReadOnlyList<BankTransaction>> CashbackAsync(Guid owner, int offset, int limit, CancellationToken ct) =>
        await ReadAsync(() => db.Transactions.AsNoTracking().Where(t => t.InitiatorId == owner && t.Kind == TransactionKind.Purchase
            && t.Status == TransactionStatus.Completed && t.CashbackMinorUnits > 0)
            .OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id).Skip(offset).Take(limit).ToListAsync(ct));

    public async Task<IReadOnlyList<SavingsGoal>> GoalsAsync(Guid owner, CancellationToken ct) =>
        await ReadAsync(() => db.Goals.AsNoTracking().Where(g => g.OwnerId == owner).OrderBy(g => g.CreatedAt).ThenBy(g => g.Id).ToListAsync(ct));

    public async Task<SavingsGoal?> GoalAsync(Guid owner, Guid id, bool forUpdate, CancellationToken ct)
    {
        if (!forUpdate) return await ReadAsync(() => db.Goals.AsNoTracking().SingleOrDefaultAsync(g => g.OwnerId == owner && g.Id == id, ct));
        return (await db.Goals.FromSqlInterpolated($"SELECT * FROM banking.goals WHERE \"Id\" = {id} AND \"OwnerId\" = {owner} FOR UPDATE").ToListAsync(ct)).SingleOrDefault();
    }

    public async Task<SavingsGoal?> AutomaticGoalAsync(Guid owner, Guid fundingAccount, CancellationToken ct) =>
        (await db.Goals.FromSqlInterpolated($"SELECT * FROM banking.goals WHERE \"OwnerId\" = {owner} AND \"FundingAccountId\" = {fundingAccount} AND NOT \"Archived\" AND \"Mode\" <> 'Off' ORDER BY \"Id\" FOR UPDATE").ToListAsync(ct)).SingleOrDefault();

    public Task<BankAccount?> SavingsAccountAsync(Guid owner, Guid id, CancellationToken ct) =>
        ReadAsync(() => db.Accounts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id && a.OwnerId == owner && a.Kind == AccountKind.Savings, ct));

    public async Task<ExchangeQuote?> QuoteAsync(Guid owner, Guid id, CancellationToken ct) =>
        (await db.Quotes.FromSqlInterpolated($"SELECT * FROM banking.exchange_quotes WHERE \"Id\" = {id} AND \"OwnerId\" = {owner} FOR UPDATE").ToListAsync(ct)).SingleOrDefault();

    public Task<IReadOnlyList<CurrencyAnalytics>> AnalyticsAsync(Guid owner, DateTimeOffset from, DateTimeOffset until, CancellationToken ct) =>
        ReadAsync<IReadOnlyList<CurrencyAnalytics>>(async () =>
        {
            await using var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
            var result = new List<CurrencyAnalytics>();
            var period = db.Transactions.AsNoTracking().Where(t => t.Status == TransactionStatus.Completed && t.CreatedAt >= from && t.CreatedAt < until);
            foreach (var currency in Currency.Supported)
            {
                var balances = await db.Accounts.Where(a => a.OwnerId == owner && a.CurrencyCode == currency)
                    .GroupBy(a => a.Kind).Select(g => new { Kind = g.Key, Balance = g.Sum(a => a.BalanceMinorUnits) }).ToListAsync(ct);
                var own = await period.Where(t => t.InitiatorId == owner && t.CurrencyCode == currency).GroupBy(_ => 1).Select(g => new
                {
                    TopUps = g.Sum(t => t.Kind == TransactionKind.TopUp ? (decimal)t.AmountMinorUnits : 0),
                    Purchases = g.Sum(t => t.Kind == TransactionKind.Purchase ? (decimal)t.AmountMinorUnits : 0),
                    Fees = g.Sum(t => (decimal)t.FeeMinorUnits),
                    Cashback = g.Sum(t => (decimal)t.CashbackMinorUnits),
                    Deposits = g.Sum(t => (t.Kind == TransactionKind.SavingsDeposit ? (decimal)t.AmountMinorUnits : 0) + t.SavedMinorUnits),
                    Withdrawals = g.Sum(t => t.Kind == TransactionKind.SavingsWithdrawal ? (decimal)t.AmountMinorUnits : 0),
                    ExchangedOut = g.Sum(t => t.Kind == TransactionKind.Exchange ? (decimal)t.AmountMinorUnits : 0),
                    Count = g.Count(t => t.Kind == TransactionKind.Purchase)
                }).SingleOrDefaultAsync(ct);
                var transfers = from t in period
                    join source in db.Accounts on t.SourceAccountId equals source.Id
                    join destination in db.Accounts on t.DestinationAccountId equals destination.Id
                    where t.CurrencyCode == currency && (t.Kind == TransactionKind.InternalTransfer || t.Kind == TransactionKind.IbanTransfer)
                    select new { t.AmountMinorUnits, SourceOwner = source.OwnerId, DestinationOwner = destination.OwnerId };
                var income = await transfers.Where(t => t.DestinationOwner == owner && t.SourceOwner != owner).SumAsync(t => (decimal)t.AmountMinorUnits, ct);
                var expenses = await transfers.Where(t => t.SourceOwner == owner && t.DestinationOwner != owner).SumAsync(t => (decimal)t.AmountMinorUnits, ct);
                var exchangedIn = await period.Where(t => t.InitiatorId == owner && t.Kind == TransactionKind.Exchange && t.DestinationCurrency == currency)
                    .SumAsync(t => (decimal)(t.ReceivedMinorUnits ?? 0), ct);
                result.Add(new CurrencyAnalytics(currency,
                    balances.Where(b => b.Kind == AccountKind.Customer).Sum(b => b.Balance) / 100m,
                    balances.Where(b => b.Kind == AccountKind.Savings).Sum(b => b.Balance) / 100m,
                    ((own?.TopUps ?? 0) + income) / 100m, ((own?.Purchases ?? 0) + expenses) / 100m,
                    (own?.Fees ?? 0) / 100m, (own?.Cashback ?? 0) / 100m, (own?.Deposits ?? 0) / 100m,
                    (own?.Withdrawals ?? 0) / 100m, (own?.ExchangedOut ?? 0) / 100m, exchangedIn / 100m, own?.Count ?? 0));
            }
            await snapshot.CommitAsync(ct);
            return result;
        });

    public void Add(SavingsGoal goal) => db.Goals.Add(goal);
    public void Add(ExchangeQuote quote) => db.Quotes.Add(quote);
}
