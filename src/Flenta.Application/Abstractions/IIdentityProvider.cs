using Flenta.Application.Features.Auth;

namespace Flenta.Application.Abstractions;

public interface IIdentityProvider
{
    Task<RegistrationResult> RegisterAsync(string email, string password, string displayName, string codeChallenge, CancellationToken cancellationToken);
    Task<AuthSession> LoginAsync(string email, string password, CancellationToken cancellationToken);
    Task<AuthSession> RefreshAsync(string refreshToken, CancellationToken cancellationToken);
    Task<AuthSession> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken cancellationToken);
    Task SendRecoveryEmailAsync(string email, string codeChallenge, CancellationToken cancellationToken);
    Task ChangePasswordAsync(string accessToken, string newPassword, CancellationToken cancellationToken);
    Task LogoutAsync(string accessToken, CancellationToken cancellationToken);
}
