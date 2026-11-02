using Flenta.Application.Abstractions;
using Flenta.Application.Abstractions.Banking;
using Flenta.Domain.Exceptions;

namespace Flenta.Application.Features.Finance;

public sealed record CurrencyAnalytics(string Currency, decimal AvailableBalance, decimal SavingsBalance, decimal Income,
    decimal Expenses, decimal Fees, decimal Cashback, decimal SavingsDeposited, decimal SavingsWithdrawn,
    decimal ExchangedOut, decimal ExchangedIn, int Purchases);
public sealed record AnalyticsResponse(DateOnly From, DateOnly Through, IReadOnlyList<CurrencyAnalytics> Currencies);

public sealed class AnalyticsService(IFinanceRepository finance, ICurrentUser user)
{
    public async Task<AnalyticsResponse> GetAsync(DateOnly from, DateOnly through, CancellationToken ct)
    {
        if (through < from || through.DayNumber - from.DayNumber > 365 || through == DateOnly.MaxValue)
            throw new DomainValidationException("analytics.invalid_period", "Choose an inclusive period of at most 366 days.");
        var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var end = new DateTimeOffset(through.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        return new AnalyticsResponse(from, through, await finance.AnalyticsAsync(user.Profile.Id, start, end, ct));
    }
}
