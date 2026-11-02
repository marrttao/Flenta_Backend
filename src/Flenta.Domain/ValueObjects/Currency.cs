using Flenta.Domain.Exceptions;

namespace Flenta.Domain.ValueObjects;

public static class Currency
{
    public static readonly IReadOnlyList<string> Supported = Array.AsReadOnly(new[] { "UAH", "USD", "EUR" });
    public static string Parse(string value)
    {
        var normalized = value?.Trim().ToUpperInvariant();
        if (normalized is not ("UAH" or "USD" or "EUR"))
            throw new DomainValidationException("currency.unsupported", "Supported currencies are UAH, USD and EUR.");
        return normalized;
    }
}
