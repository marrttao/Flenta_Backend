using Flenta.Domain.Exceptions;
using Flenta.Domain.ValueObjects;

namespace Flenta.Domain.Entities;

public sealed class ExchangeQuote : Entity
{
    private ExchangeQuote() : base(Guid.NewGuid()) { }
    public ExchangeQuote(Guid ownerId, Guid source, Guid destination, string sourceCurrency, string destinationCurrency,
        Money amount, decimal rate, DateOnly rateDate, DateTimeOffset now) : base(Guid.NewGuid())
    {
        amount.RequirePositive();
        if (source == destination || rate <= 0 || Currency.Parse(sourceCurrency) == Currency.Parse(destinationCurrency))
            throw new DomainValidationException("exchange.invalid", "Use different accounts and currencies.");
        var received = Money.FromDecimal(decimal.Round(amount.Amount * rate, 2, MidpointRounding.AwayFromZero));
        received.RequirePositive();
        OwnerId = ownerId; SourceAccountId = source; DestinationAccountId = destination;
        SourceCurrency = sourceCurrency; DestinationCurrency = destinationCurrency;
        AmountMinorUnits = amount.MinorUnits; ReceivedMinorUnits = received.MinorUnits;
        Rate = rate; RateDate = rateDate; CreatedAt = now; ExpiresAt = now.AddMinutes(5);
    }
    public Guid OwnerId { get; private set; }
    public Guid SourceAccountId { get; private set; }
    public Guid DestinationAccountId { get; private set; }
    public string SourceCurrency { get; private set; } = "";
    public string DestinationCurrency { get; private set; } = "";
    public long AmountMinorUnits { get; private set; }
    public long ReceivedMinorUnits { get; private set; }
    public decimal Rate { get; private set; }
    public DateOnly RateDate { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public Guid? TransactionId { get; private set; }
    public void Consume(Guid transactionId, DateTimeOffset now)
    {
        if (TransactionId is not null || now >= ExpiresAt)
            throw new DomainValidationException("exchange.quote_unavailable", "Exchange quote has expired or was already used. Request a new quote.");
        TransactionId = transactionId;
    }
}
