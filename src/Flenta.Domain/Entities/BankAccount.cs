using Flenta.Domain.Exceptions;
using Flenta.Domain.ValueObjects;

namespace Flenta.Domain.Entities;

public enum AccountKind { Customer, SandboxClearing, FeeRevenue, Savings, SandboxMerchant }

public sealed class BankAccount : Entity
{
    private BankAccount() : base(Guid.NewGuid()) { }
    private BankAccount(Guid id, Guid? ownerId, string? iban, AccountKind kind, DateTimeOffset createdAt) : base(id)
    {
        OwnerId = ownerId;
        Iban = iban;
        Kind = kind;
        CreatedAt = createdAt;
    }

    public Guid? OwnerId { get; private set; }
    public string? Iban { get; private set; }
    public AccountKind Kind { get; private set; }
    public string CurrencyCode { get; private set; } = "UAH";
    public long BalanceMinorUnits { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static BankAccount Open(Guid ownerId, DateTimeOffset now, string currency = "UAH")
    {
        if (ownerId == Guid.Empty) throw new ArgumentException("Owner is required.", nameof(ownerId));
        var id = Guid.NewGuid();
        return new BankAccount(id, ownerId, ValueObjects.Iban.CreateSandbox(id).Value, AccountKind.Customer, now)
        { CurrencyCode = Currency.Parse(currency) };
    }

    public static BankAccount OpenSavings(Guid ownerId, string currency, DateTimeOffset now)
    {
        if (ownerId == Guid.Empty) throw new ArgumentException("Owner is required.", nameof(ownerId));
        return new(Guid.NewGuid(), ownerId, null, AccountKind.Savings, now) { CurrencyCode = Currency.Parse(currency) };
    }

    public void EnsureCanDebit(Money amount)
    {
        amount.RequirePositive();
        if (Kind != AccountKind.SandboxClearing && BalanceMinorUnits < amount.MinorUnits)
            throw new DomainValidationException("account.insufficient_funds", "Insufficient funds including the fee.");
        if (BalanceMinorUnits < long.MinValue + amount.MinorUnits)
            throw new DomainValidationException("account.balance_limit", "Account balance limit reached.");
    }

    public void EnsureCanCredit(Money amount)
    {
        amount.RequirePositive();
        if (BalanceMinorUnits > Money.MaximumMinorUnits - amount.MinorUnits)
            throw new DomainValidationException("account.balance_limit", "Account balance limit reached.");
    }

    public void Debit(Money amount) { EnsureCanDebit(amount); BalanceMinorUnits -= amount.MinorUnits; }
    public void Credit(Money amount) { EnsureCanCredit(amount); BalanceMinorUnits += amount.MinorUnits; }
}
