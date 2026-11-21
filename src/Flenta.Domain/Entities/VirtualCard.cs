using Flenta.Domain.Exceptions;
using Flenta.Domain.ValueObjects;

namespace Flenta.Domain.Entities;

public enum CardStatus { Active, Blocked, Closed }

public sealed class VirtualCard : Entity
{
    private VirtualCard() : base(Guid.NewGuid()) { }
    public VirtualCard(Guid id, Guid accountId, string last4, string protectedData, string fingerprint, int expiryMonth, int expiryYear, DateTimeOffset now) : base(id)
    {
        if (accountId == Guid.Empty || last4.Length != 4 || last4.Any(c => !char.IsAsciiDigit(c))
            || string.IsNullOrWhiteSpace(protectedData) || string.IsNullOrWhiteSpace(fingerprint)
            || expiryMonth is < 1 or > 12 || expiryYear is < 2000 or > 9998)
            throw new DomainValidationException("card.invalid", "Card data is invalid.");
        AccountId = accountId;
        Last4 = last4;
        ProtectedData = protectedData;
        Fingerprint = fingerprint;
        ExpiryMonth = expiryMonth;
        ExpiryYear = expiryYear;
        CreatedAt = now;
    }

    public Guid AccountId { get; private set; }
    public string Last4 { get; private set; } = "";
    public string ProtectedData { get; private set; } = "";
    public string Fingerprint { get; private set; } = "";
    public int ExpiryMonth { get; private set; }
    public int ExpiryYear { get; private set; }
    public CardStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastSecretsViewedAt { get; private set; }
    public bool AdminFrozen { get; private set; }
    public void SetAdminFrozen(bool frozen) => AdminFrozen = frozen;
    public bool OnlinePayments { get; private set; } = true;
    public bool ContactlessPayments { get; private set; } = true;
    public bool CashWithdrawals { get; private set; } = true;
    public long MonthlyLimitMinorUnits { get; private set; } = Money.MaximumMinorUnits;
    public long SpentMinorUnits { get; private set; }
    public int SpendingMonth { get; private set; }
    public void Configure(bool online, bool contactless, bool cash, decimal limit)
    {
        EnsureNotClosed();
        var money = Money.FromDecimal(limit); money.RequirePositive();
        OnlinePayments = online; ContactlessPayments = contactless; CashWithdrawals = cash; MonthlyLimitMinorUnits = money.MinorUnits;
    }
    public void EnsureCanPurchase(Money amount, string channel, DateTimeOffset now)
    {
        EnsureUsable(now);
        if (channel is not ("Online" or "Contactless" or "CashWithdrawal") || channel == "Online" && !OnlinePayments || channel == "Contactless" && !ContactlessPayments || channel == "CashWithdrawal" && !CashWithdrawals)
            throw new DomainValidationException("card.channel_disabled", "This payment channel is disabled.");
        var month = now.UtcDateTime.Year * 100 + now.UtcDateTime.Month;
        var spent = SpendingMonth == month ? SpentMinorUnits : 0;
        if (amount.MinorUnits > MonthlyLimitMinorUnits - spent)
            throw new DomainValidationException("card.monthly_limit", "The monthly card spending limit would be exceeded.");
    }
    public void RecordPurchase(Money amount, DateTimeOffset now)
    {
        var month = now.UtcDateTime.Year * 100 + now.UtcDateTime.Month;
        if (SpendingMonth != month) { SpendingMonth = month; SpentMinorUnits = 0; }
        SpentMinorUnits = checked(SpentMinorUnits + amount.MinorUnits);
    }
    public string MaskedNumber => "**** **** **** " + Last4;

    public void Block() { EnsureNotClosed(); Status = CardStatus.Blocked; }
    public void Unblock() { EnsureNotClosed(); Status = CardStatus.Active; }
    public void Close() => Status = CardStatus.Closed;
    public void MarkSecretsViewed(DateTimeOffset now)
    {
        EnsureUsable(now);
        LastSecretsViewedAt = now;
    }

    public void EnsureUsable(DateTimeOffset now)
    {
        if (AdminFrozen || Status != CardStatus.Active || now >= new DateTimeOffset(ExpiryYear, ExpiryMonth, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1))
            throw new DomainValidationException("card.unavailable", "The card is blocked, closed or expired.");
    }

    private void EnsureNotClosed()
    {
        if (Status == CardStatus.Closed) throw new DomainValidationException("card.closed", "A closed card cannot be reactivated.");
    }
}
