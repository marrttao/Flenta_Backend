using System.Numerics;
using System.Text;
using Flenta.Domain.Exceptions;

namespace Flenta.Domain.ValueObjects;

public sealed record Iban
{
    private Iban(string value) => Value = value;
    public string Value { get; }

    public static Iban Parse(string? value)
    {
        var normalized = value?.Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();
        if (normalized is null || normalized.Length != 29 || !normalized.StartsWith("UA", StringComparison.Ordinal)
            || normalized[2..].Any(c => c is < '0' or > '9') || Remainder(normalized) != 1)
            throw new DomainValidationException("iban.invalid", "A valid Ukrainian IBAN is required.");
        return new Iban(normalized);
    }

    public static Iban CreateSandbox(Guid accountId)
    {
        // Reserved application routing code; these accounts are never routed to a real bank.
        var number = (new BigInteger(accountId.ToByteArray(), isUnsigned: true) % BigInteger.Pow(10, 19)).ToString("D19");
        var bban = "999999" + number;
        var check = 98 - Remainder("UA00" + bban);
        return Parse("UA" + check.ToString("D2") + bban);
    }

    private static int Remainder(string value)
    {
        var expanded = new StringBuilder();
        foreach (var c in value[4..] + value[..4]) expanded.Append(char.IsAsciiLetterUpper(c) ? (c - 'A' + 10).ToString() : c.ToString());
        var remainder = 0;
        foreach (var c in expanded.ToString()) remainder = (remainder * 10 + c - '0') % 97;
        return remainder;
    }
}
