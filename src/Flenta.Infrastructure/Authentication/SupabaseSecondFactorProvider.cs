using System.Text.Json;
using Flenta.Application.Abstractions;
using Flenta.Application.Features.Auth;
using Flenta.Domain.Exceptions;
namespace Flenta.Infrastructure.Authentication;

internal sealed class SupabaseSecondFactorProvider(SupabaseAuthClient client) : ISecondFactorProvider
{
    public async Task<IReadOnlyList<MfaFactor>> FactorsAsync(string token, CancellationToken ct)
    {
        var data = await client.SendAsync(HttpMethod.Get, "user", null, token, ct);
        if (!data.TryGetProperty("factors", out var factors)) return [];
        if (factors.ValueKind != JsonValueKind.Array) throw SupabaseAuthClient.Unavailable();
        return factors.EnumerateArray().Where(f => String(f, "factor_type") == "totp").Select(f => new MfaFactor(Id(f, "id"), String(f, "friendly_name"), String(f, "status"))).ToArray();
    }
    public async Task<MfaEnrollment> EnrollAsync(string token, CancellationToken ct)
    {
        var data = await client.SendAsync(HttpMethod.Post, "factors", new { factor_type = "totp", friendly_name = "Flenta " + Guid.NewGuid().ToString("N")[..8], issuer = "Flenta" }, token, ct);
        if (!data.TryGetProperty("totp", out var totp)) throw SupabaseAuthClient.Unavailable();
        var secret = String(totp, "secret"); if (secret.Length is < 16 or > 128 || secret.Any(c => !char.IsAsciiLetterOrDigit(c))) throw SupabaseAuthClient.Unavailable();
        return new(Id(data, "id"), secret);
    }
    public async Task<AuthSession> VerifyAsync(string token, Guid factor, string code, CancellationToken ct)
    {
        if (factor == Guid.Empty || code.Length != 6 || code.Any(c => !char.IsAsciiDigit(c))) throw new DomainValidationException("auth.invalid_mfa_code", "Enter the six-digit authenticator code.");
        var challenge = await client.SendAsync(HttpMethod.Post, $"factors/{factor:D}/challenge", new { }, token, ct);
        return SupabaseAuthClient.ReadSession(await client.SendAsync(HttpMethod.Post, $"factors/{factor:D}/verify", new { challenge_id = Id(challenge, "id"), code }, token, ct));
    }
    public async Task RemoveAsync(string token, Guid factor, CancellationToken ct) => _ = await client.SendAsync(HttpMethod.Delete, $"factors/{factor:D}", null, token, ct);
    public async Task SignOutOthersAsync(string token, CancellationToken ct) => _ = await client.SendAsync(HttpMethod.Post, "logout?scope=others", null, token, ct);
    private static string String(JsonElement obj, string key) => obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
    private static Guid Id(JsonElement obj, string key) => Guid.TryParse(String(obj, key), out var id) && id != Guid.Empty ? id : throw SupabaseAuthClient.Unavailable();
}
