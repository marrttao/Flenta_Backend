using Flenta.Application.Features.Finance;
using Flenta.Domain.Entities;

namespace Flenta.Application.Abstractions.Banking;

public interface IFinanceRepository
{
    Task<IReadOnlyList<SavingsGoal>> GoalsAsync(Guid owner, CancellationToken cancellationToken);
    Task<SavingsGoal?> GoalAsync(Guid owner, Guid id, bool forUpdate, CancellationToken cancellationToken);
    Task<SavingsGoal?> AutomaticGoalAsync(Guid owner, Guid fundingAccount, CancellationToken cancellationToken);
    Task<BankAccount?> SavingsAccountAsync(Guid owner, Guid id, CancellationToken cancellationToken);
    Task<ExchangeQuote?> QuoteAsync(Guid owner, Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<BankTransaction>> CashbackAsync(Guid owner, int offset, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<CurrencyAnalytics>> AnalyticsAsync(Guid owner, DateTimeOffset from, DateTimeOffset until, CancellationToken cancellationToken);
    void Add(SavingsGoal goal);
    void Add(ExchangeQuote quote);
}

public sealed record CurrencyRates(DateOnly Date, string Source, IReadOnlyDictionary<string, decimal> UahPerUnit);
public interface IExchangeRateProvider
{
    Task<CurrencyRates> GetAsync(CancellationToken cancellationToken);
}
