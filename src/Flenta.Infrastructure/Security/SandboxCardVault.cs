using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Flenta.Application.Abstractions.Banking;
using Flenta.Application.Features.Auth;
using Flenta.Infrastructure.Persistence.Banking;
using Microsoft.Extensions.Options;

namespace Flenta.Infrastructure.Security;

public sealed class SandboxCardVault(IOptions<BankingOptions> options) : ICardVault
{
    public ProtectedCardData Create(Guid owner, Guid cardId)
    {
        var body = "999999" + string.Concat(Enumerable.Range(0, 9).Select(_ => RandomNumberGenerator.GetInt32(10).ToString()));
        var number = body + CheckDigit(body);
        var cvv = RandomNumberGenerator.GetInt32(1000).ToString("D3");
        var key = Key();
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(new CardSecrets(number, cvv));
        try
        {
            var nonce = RandomNumberGenerator.GetBytes(12);
            var tag = new byte[16];
            var ciphertext = new byte[plaintext.Length];
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, AssociatedData(owner, cardId));
            var envelope = nonce.Concat(tag).Concat(ciphertext).ToArray();
            var fingerprint = Convert.ToHexString(HMACSHA256.HashData(key, Encoding.ASCII.GetBytes(number)));
            return new ProtectedCardData(number[^4..], "v1." + Convert.ToBase64String(envelope), fingerprint);
        }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plaintext); }
    }

    public CardSecrets Reveal(Guid owner, Guid cardId, string ciphertext)
    {
        var key = Key();
        byte[]? plaintext = null;
        try
        {
            if (!ciphertext.StartsWith("v1.", StringComparison.Ordinal)) throw new CryptographicException();
            var envelope = Convert.FromBase64String(ciphertext[3..]);
            if (envelope.Length <= 28) throw new CryptographicException();
            plaintext = new byte[envelope.Length - 28];
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(envelope.AsSpan(0, 12), envelope.AsSpan(28), envelope.AsSpan(12, 16), plaintext, AssociatedData(owner, cardId));
            return JsonSerializer.Deserialize<CardSecrets>(plaintext) ?? throw new CryptographicException();
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException or JsonException)
        {
            throw new ApplicationFailureException(FailureKind.Unavailable, "card.secrets_unavailable", "Card requisites are unavailable.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private byte[] Key()
    {
        if (!options.Value.Enabled || !BankingOptions.ValidKey(options.Value.CardEncryptionKey))
            throw new ApplicationFailureException(FailureKind.Unavailable, "banking.not_configured", "Banking service is unavailable.");
        return Convert.FromBase64String(options.Value.CardEncryptionKey);
    }

    private static byte[] AssociatedData(Guid owner, Guid card) => Encoding.UTF8.GetBytes($"{owner:N}:{card:N}");
    private static int CheckDigit(string body)
    {
        var sum = 0;
        for (var index = 0; index < body.Length; index++)
        {
            var digit = body[index] - '0';
            if (index % 2 == 0) { digit *= 2; if (digit > 9) digit -= 9; }
            sum += digit;
        }
        return (10 - sum % 10) % 10;
    }
}
