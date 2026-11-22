using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Flenta.Application.Abstractions.Kyc;
using Flenta.Application.Features.Auth;
using Flenta.Infrastructure.Persistence.Banking;
using Microsoft.Extensions.Options;
namespace Flenta.Infrastructure.Security;

internal sealed class IdentityVault(IOptions<BankingOptions> options) : IIdentityVault
{
    private byte[] Key()
    {
        if (!BankingOptions.ValidKey(options.Value.CardEncryptionKey)) throw new ApplicationFailureException(FailureKind.Unavailable, "kyc.encryption_not_configured", "Identity encryption is not configured.");
        return Convert.FromBase64String(options.Value.CardEncryptionKey);
    }
    private static byte[] Context(Guid verification) => Encoding.UTF8.GetBytes($"flenta-identity-v1:{verification:N}");
    public string TaxFingerprint(string taxNumber)
    {
        var normalized = Flenta.Domain.ValueObjects.Rnokpp.Parse(taxNumber).Value; var key = Key();
        try { return Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes("flenta-tax-search-v1:" + normalized))); }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
    public ProtectedIdentityData Seal(Guid verification, KycIdentityData identity)
    {
        var key = Key(); var plaintext = JsonSerializer.SerializeToUtf8Bytes(identity);
        try
        {
            var nonce = RandomNumberGenerator.GetBytes(12); var tag = new byte[16]; var cipher = new byte[plaintext.Length];
            using var aes = new AesGcm(key, 16); aes.Encrypt(nonce, plaintext, cipher, tag, Context(verification));
            var digestInput = Context(verification).Concat(plaintext).ToArray(); string digest;
            try { digest = Convert.ToHexString(HMACSHA256.HashData(key, digestInput)); } finally { CryptographicOperations.ZeroMemory(digestInput); }
            return new("v1." + Convert.ToBase64String(nonce.Concat(tag).Concat(cipher).ToArray()), digest, identity.TaxNumber is null ? null : TaxFingerprint(identity.TaxNumber));
        }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plaintext); }
    }
    public KycIdentityData Open(Guid verification, string ciphertext)
    {
        var key = Key(); byte[]? plain = null;
        try
        {
            if (!ciphertext.StartsWith("v1.", StringComparison.Ordinal)) throw new CryptographicException();
            var value = Convert.FromBase64String(ciphertext[3..]); if (value.Length <= 28) throw new CryptographicException();
            plain = new byte[value.Length - 28]; using var aes = new AesGcm(key, 16);
            aes.Decrypt(value.AsSpan(0, 12), value.AsSpan(28), value.AsSpan(12, 16), plain, Context(verification));
            return JsonSerializer.Deserialize<KycIdentityData>(plain) ?? throw new CryptographicException();
        }
        catch (Exception e) when (e is CryptographicException or FormatException or JsonException) { throw new ApplicationFailureException(FailureKind.Unavailable, "kyc.identity_unavailable", "Verified identity details are unavailable."); }
        finally { CryptographicOperations.ZeroMemory(key); if (plain is not null) CryptographicOperations.ZeroMemory(plain); }
    }
}
