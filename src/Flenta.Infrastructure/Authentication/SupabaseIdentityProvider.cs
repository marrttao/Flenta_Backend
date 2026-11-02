using Flenta.Application.Abstractions;
using Flenta.Application.Features.Auth;

namespace Flenta.Infrastructure.Authentication;

internal sealed class SupabaseIdentityProvider(SupabaseAuthClient client) : IIdentityProvider
{
    public async Task<RegistrationResult> RegisterAsync(string email, string password, string displayName, string codeChallenge, CancellationToken cancellationToken)
    {
        var result = await client.SendAsync(HttpMethod.Post, "signup?redirect_to=" + Uri.EscapeDataString(client.RedirectTo("/auth/confirm-email")),
            new { email, password, data = new { display_name = displayName }, code_challenge = codeChallenge, code_challenge_method = "s256" }, null, cancellationToken);
        if (result.TryGetProperty("access_token", out var token) && token.ValueKind == System.Text.Json.JsonValueKind.String && !string.IsNullOrEmpty(token.GetString()))
            return new RegistrationResult(false, SupabaseAuthClient.ReadSession(result));
        return new RegistrationResult(true, null);
    }

    public async Task<AuthSession> LoginAsync(string email, string password, CancellationToken cancellationToken) =>
        SupabaseAuthClient.ReadSession(await client.SendAsync(HttpMethod.Post, "token?grant_type=password", new { email, password }, null, cancellationToken));

    public async Task<AuthSession> RefreshAsync(string refreshToken, CancellationToken cancellationToken) =>
        SupabaseAuthClient.ReadSession(await client.SendAsync(HttpMethod.Post, "token?grant_type=refresh_token", new { refresh_token = refreshToken }, null, cancellationToken));

    public async Task<AuthSession> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken cancellationToken) =>
        SupabaseAuthClient.ReadSession(await client.SendAsync(HttpMethod.Post, "token?grant_type=pkce", new { auth_code = code, code_verifier = codeVerifier }, null, cancellationToken));

    public async Task SendRecoveryEmailAsync(string email, string codeChallenge, CancellationToken cancellationToken)
    {
        await client.SendAsync(HttpMethod.Post, "recover?redirect_to=" + Uri.EscapeDataString(client.RedirectTo("/auth/reset-password")),
            new { email, code_challenge = codeChallenge, code_challenge_method = "s256" }, null, cancellationToken);
    }

    public async Task ChangePasswordAsync(string accessToken, string newPassword, CancellationToken cancellationToken)
    {
        await client.SendAsync(HttpMethod.Put, "user", new { password = newPassword }, accessToken, cancellationToken);
    }

    public async Task LogoutAsync(string accessToken, CancellationToken cancellationToken)
    {
        await client.SendAsync(HttpMethod.Post, "logout?scope=local", null, accessToken, cancellationToken);
    }
}
