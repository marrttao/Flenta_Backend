using Flenta.Domain.Exceptions;

namespace Flenta.Domain.ValueObjects;

/// <summary>An immutable Ukrainian taxpayer registration number (RNOKPP): ten digits ending in a control digit.</summary>
public sealed record Rnokpp
{
    private static readonly int[] Weights = [-1, 5, 7, 9, 4, 6, 10, 5, 7];

    private Rnokpp(string value) => Value = value;

    public string Value { get; }

    public static Rnokpp Parse(string? value)
    {
        var candidate = value?.Trim();
        if (candidate is null || candidate.Length != 10 || candidate.Any(c => c is < '0' or > '9')
            || ControlDigit(candidate) != candidate[9] - '0')
            throw new DomainValidationException("rnokpp.invalid", "A valid ten-digit taxpayer number is required.");
        return new Rnokpp(candidate);
    }

    private static int ControlDigit(string digits)
    {
        var sum = 0;
        for (var i = 0; i < Weights.Length; i++) sum += (digits[i] - '0') * Weights[i];
        return (sum % 11 + 11) % 11 % 10;
    }

    public override string ToString() => Value;
}
