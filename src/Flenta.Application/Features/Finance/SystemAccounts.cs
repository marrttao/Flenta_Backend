using Flenta.Domain.ValueObjects;

namespace Flenta.Application.Features.Finance;

public static class SystemAccounts
{
    public static Guid Clearing(string currency) => Id(currency, 1);
    public static Guid Fees(string currency) => Id(currency, 2);
    public static Guid Merchant(string currency) => Id(currency, 3);
    private static Guid Id(string currency, int kind)
    {
        var offset = Currency.Parse(currency) switch { "USD" => 10, "EUR" => 20, _ => 0 };
        return Guid.Parse($"00000000-0000-0000-0000-{offset + kind:000000000000}");
    }
}
