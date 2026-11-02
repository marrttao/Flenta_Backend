using Flenta.Application.Abstractions;
using Flenta.Application.Abstractions.Banking;
using Flenta.Application.Features.Accounts;
using Flenta.Application.Features.Transactions;
using Flenta.Domain.Entities;
using Flenta.Domain.Exceptions;
using Flenta.Domain.ValueObjects;

namespace Flenta.Application.Features.Finance;

public sealed record ExchangeQuoteResponse(Guid Id, Guid SourceAccountId, Guid DestinationAccountId, string SourceCurrency,
    string DestinationCurrency, decimal Amount, decimal ReceivedAmount, decimal Rate, DateOnly RateDate,
    decimal Fee, DateTimeOffset ExpiresAt, string RateSource);

public sealed class ExchangeService(IFinanceRepository finance, IBankingRepository bank, IBankingUnitOfWork unitOfWork,
    IExchangeRateProvider rates, ICurrentUser user, IClock clock)
{
    public Task<CurrencyRates> RatesAsync(CancellationToken ct) => rates.GetAsync(ct);
    public async Task<ExchangeQuoteResponse> QuoteAsync(Guid sourceId, Guid destinationId, decimal value, CancellationToken ct)
    {
        var amount = Money.FromDecimal(value); amount.RequirePositive();
        var source = await bank.OwnedAccountAsync(user.Profile.Id, sourceId, ct) ?? throw AccountService.Missing();
        var destination = await bank.OwnedAccountAsync(user.Profile.Id, destinationId, ct) ?? throw AccountService.Missing();
        if (source.CurrencyCode == destination.CurrencyCode)
            throw new DomainValidationException("exchange.same_currency", "Choose accounts in different currencies.");
        var snapshot = await rates.GetAsync(ct);
        var rate = decimal.Round(snapshot.UahPerUnit[source.CurrencyCode] / snapshot.UahPerUnit[destination.CurrencyCode], 12, MidpointRounding.AwayFromZero);
        return await unitOfWork.ExecuteAsync(user.Profile.Id, Guid.NewGuid(), _ =>
        {
            var quote = new ExchangeQuote(user.Profile.Id, source.Id, destination.Id, source.CurrencyCode, destination.CurrencyCode,
                amount, rate, snapshot.Date, clock.UtcNow);
            finance.Add(quote);
            return Task.FromResult(new ExchangeQuoteResponse(quote.Id, source.Id, destination.Id, source.CurrencyCode,
                destination.CurrencyCode, amount.Amount, quote.ReceivedMinorUnits / 100m, rate, snapshot.Date, 0, quote.ExpiresAt, snapshot.Source));
        }, ct);
    }

    public Task<TransactionResponse> ExchangeAsync(Guid quoteId, Guid key, CancellationToken ct)
    {
        FinanceOperation.RequireKey(key);
        var fingerprint = FinanceOperation.Fingerprint($"Exchange|{quoteId:N}");
        return unitOfWork.ExecuteAsync(user.Profile.Id, key, async token =>
        {
            var existing = await FinanceOperation.ExistingAsync(bank, user.Profile.Id, key, fingerprint, token);
            if (existing is not null) return existing;
            var quote = await finance.QuoteAsync(user.Profile.Id, quoteId, token) ?? throw AccountService.Missing();
            var amount = Money.FromMinorUnits(quote.AmountMinorUnits);
            var received = Money.FromMinorUnits(quote.ReceivedMinorUnits);
            var operation = new BankTransaction(user.Profile.Id, key, fingerprint, TransactionKind.Exchange, quote.SourceAccountId,
                quote.DestinationAccountId, null, amount, Money.FromMinorUnits(0), clock.UtcNow, quote.SourceCurrency);
            quote.Consume(operation.Id, clock.UtcNow);
            operation.DescribeExchange(quote.DestinationCurrency, received, quote.Rate, quote.RateDate);
            bank.Add(operation);
            var ids = new[] { quote.SourceAccountId, quote.DestinationAccountId,
                SystemAccounts.Clearing(quote.SourceCurrency), SystemAccounts.Clearing(quote.DestinationCurrency) };
            var accounts = (await bank.LockAccountsAsync(ids, token)).ToDictionary(a => a.Id);
            try
            {
                bank.AddEntries(PostingPlan.Apply(operation, accounts,
                [
                    (quote.SourceAccountId, -amount.MinorUnits), (SystemAccounts.Clearing(quote.SourceCurrency), amount.MinorUnits),
                    (SystemAccounts.Clearing(quote.DestinationCurrency), -received.MinorUnits), (quote.DestinationAccountId, received.MinorUnits)
                ], clock.UtcNow));
            }
            catch (DomainValidationException exception) when (operation.Status == TransactionStatus.Pending) { operation.Reject(exception.Code, clock.UtcNow); }
            return TransactionService.ToResponse(operation);
        }, ct);
    }
}
