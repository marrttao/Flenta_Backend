using Flenta.Domain.Exceptions;

namespace Flenta.Domain.ValueObjects;

public readonly record struct Money
{
    public const long MaximumMinorUnits = 1_000_000_000_000;
    public const string Currency = "UAH";
    private Money(long minorUnits) => MinorUnits = minorUnits;
    public long MinorUnits { get; }
    public decimal Amount => MinorUnits / 100m;

    public static Money FromMinorUnits(long value)
    {
        if (value < 0 || value > MaximumMinorUnits)
            throw new DomainValidationException("money.out_of_range", "Amount is outside the supported range.");
        return new Money(value);
    }

    public static Money FromDecimal(decimal value)
    {
        if (value < 0 || value > MaximumMinorUnits / 100m || decimal.Round(value, 2) != value)
            throw new DomainValidationException("money.invalid_amount", "Amount must have at most two decimal places and be within the supported range.");
        return FromMinorUnits((long)(value * 100m));
    }

    public Money Add(Money other) => FromMinorUnits(checked(MinorUnits + other.MinorUnits));
    public void RequirePositive()
    {
        if (MinorUnits == 0) throw new DomainValidationException("money.must_be_positive", "Amount must be greater than zero.");
    }
}
