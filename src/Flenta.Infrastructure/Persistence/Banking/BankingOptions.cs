using System.Security.Cryptography;

namespace Flenta.Infrastructure.Persistence.Banking;

public sealed class BankingOptions
{
    public bool Enabled { get; set; }
    public string ConnectionString { get; set; } = "";
    public string CardEncryptionKey { get; set; } = "";
    public decimal IbanFeeRate { get; set; } = 0.005m;
    public long MinimumIbanFeeMinorUnits { get; set; } = 100;

    public static bool ValidKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        try
        {
            var bytes = Convert.FromBase64String(value);
            var valid = bytes.Length == 32;
            CryptographicOperations.ZeroMemory(bytes);
            return valid;
        }
        catch (FormatException) { return false; }
    }
}
