using System.Text.RegularExpressions;
using Flenta.Application.Abstractions;
using Flenta.Domain.Entities;
using Flenta.Domain.Exceptions;
using Flenta.Domain.ValueObjects;

namespace Flenta.Application.Features.Auth;

public sealed partial class AuthenticationService(IIdentityProvider identityProvider)
{
    public Task<RegistrationResult> RegisterAsync(string email, string password, string displayName, string codeChallenge, CancellationToken cancellationToken)
    {
        var address = EmailAddress.Create(email);
        ValidatePassword(password);
        var name = UserProfile.NormalizeDisplayName(displayName);
        ValidateChallenge(codeChallenge);
        return identityProvider.RegisterAsync(address.Value, password, name, codeChallenge, cancellationToken);
    }

    public Task<AuthSession> LoginAsync(string email, string password, CancellationToken cancellationToken)
    {
        var address = EmailAddress.Create(email);
        RequireValue(password, "password", 128);
        return identityProvider.LoginAsync(address.Value, password, cancellationToken);
    }

    public Task<AuthSession> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        RequireValue(refreshToken, "refresh_token", 4096);
        return identityProvider.RefreshAsync(refreshToken, cancellationToken);
    }

    public Task<AuthSession> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken cancellationToken)
    {
        RequireValue(code, "code", 4096);
        if (codeVerifier is null || !VerifierPattern().IsMatch(codeVerifier))
        {
            throw new DomainValidationException("auth.invalid_verifier", "A valid PKCE verifier is required.");
        }
        return identityProvider.ExchangeCodeAsync(code, codeVerifier, cancellationToken);
    }

    public Task RecoverAsync(string email, string codeChallenge, CancellationToken cancellationToken)
    {
        var address = EmailAddress.Create(email);
        ValidateChallenge(codeChallenge);
        return identityProvider.SendRecoveryEmailAsync(address.Value, codeChallenge, cancellationToken);
    }

    public Task ChangePasswordAsync(string accessToken, string newPassword, CancellationToken cancellationToken)
    {
        ValidatePassword(newPassword);
        return identityProvider.ChangePasswordAsync(accessToken, newPassword, cancellationToken);
    }

    public Task LogoutAsync(string accessToken, CancellationToken cancellationToken) => identityProvider.LogoutAsync(accessToken, cancellationToken);

    private static void ValidatePassword(string? password)
    {
        if (password is null || password.Length is < 12 or > 128)
        {
            throw new DomainValidationException("password.invalid_length", "Password must contain 12–128 characters.");
        }
    }

    private static void ValidateChallenge(string? challenge)
    {
        if (challenge is null || !ChallengePattern().IsMatch(challenge))
        {
            throw new DomainValidationException("auth.invalid_challenge", "A valid S256 PKCE challenge is required.");
        }
    }

    private static void RequireValue(string? value, string field, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength)
        {
            throw new DomainValidationException($"auth.invalid_{field}", $"A valid {field} is required.");
        }
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{43}$", RegexOptions.CultureInvariant)]
    private static partial Regex ChallengePattern();

    [GeneratedRegex("^[A-Za-z0-9._~-]{43,128}$", RegexOptions.CultureInvariant)]
    private static partial Regex VerifierPattern();
}
