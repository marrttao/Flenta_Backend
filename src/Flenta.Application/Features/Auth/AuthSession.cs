namespace Flenta.Application.Features.Auth;

public sealed record AuthSession(string AccessToken, string RefreshToken, long ExpiresIn, ProfileResponse User);
public sealed record RegistrationResult(bool RequiresEmailConfirmation, AuthSession? Session);
public sealed record ProfileResponse(Guid Id, string Email, string DisplayName, bool EmailConfirmed);
