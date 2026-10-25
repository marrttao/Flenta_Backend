using Flenta.Domain.Exceptions;

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
    public string MaskedNumber => "**** **** **** " + Last4;

    public void Block() { EnsureNotClosed(); Status = CardStatus.Blocked; }
    public void Unblock() { EnsureNotClosed(); Status = CardStatus.Active; }
    public void Close() => Status = CardStatus.Closed;
    public void MarkSecretsViewed(DateTimeOffset now)
    {
        if (Status != CardStatus.Active || now >= new DateTimeOffset(ExpiryYear, ExpiryMonth, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1))
            throw new DomainValidationException("card.unavailable", "Requisites are unavailable for a blocked, closed or expired card.");
        LastSecretsViewedAt = now;
    }

    private void EnsureNotClosed()
    {
        if (Status == CardStatus.Closed) throw new DomainValidationException("card.closed", "A closed card cannot be reactivated.");
    }
}
