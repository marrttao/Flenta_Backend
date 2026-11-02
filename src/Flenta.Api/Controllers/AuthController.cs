using System.ComponentModel.DataAnnotations;
using Flenta.Application.Abstractions;
using Flenta.Application.Features.Auth;
using Flenta.Api.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Flenta.Api.Controllers;

[ApiController]
[Route("api/auth")]
[EnableRateLimiting("auth")]
[RequestSizeLimit(16 * 1024)]
public sealed class AuthController(AuthenticationService authentication, ICurrentUser currentUser) : ControllerBase
{
    [AllowAnonymous, HttpPost("register")]
    public Task<RegistrationResult> Register(RegisterRequest request, CancellationToken cancellationToken) =>
        authentication.RegisterAsync(request.Email, request.Password, request.DisplayName, request.CodeChallenge, cancellationToken);

    [AllowAnonymous, HttpPost("login")]
    public Task<AuthSession> Login(LoginRequest request, CancellationToken cancellationToken) =>
        authentication.LoginAsync(request.Email, request.Password, cancellationToken);

    [AllowAnonymous, HttpPost("refresh")]
    public Task<AuthSession> Refresh(RefreshRequest request, CancellationToken cancellationToken) =>
        authentication.RefreshAsync(request.RefreshToken, cancellationToken);

    [AllowAnonymous, HttpPost("exchange")]
    public Task<AuthSession> Exchange(ExchangeRequest request, CancellationToken cancellationToken) =>
        authentication.ExchangeCodeAsync(request.Code, request.CodeVerifier, cancellationToken);

    [AllowAnonymous, HttpPost("recover")]
    public async Task<IActionResult> Recover(RecoveryRequest request, CancellationToken cancellationToken)
    {
        await authentication.RecoverAsync(request.Email, request.CodeChallenge, cancellationToken);
        return Accepted(new { message = "If the address is registered, a recovery email will be sent." });
    }

    [Authorize(Policy = KycPolicies.Onboarding)]
    [HttpPut("password")]
    public async Task<IActionResult> ChangePassword(PasswordRequest request, CancellationToken cancellationToken)
    {
        await authentication.ChangePasswordAsync(currentUser.AccessToken, request.NewPassword, cancellationToken);
        return NoContent();
    }

    [Authorize(Policy = KycPolicies.Onboarding)]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await authentication.LogoutAsync(currentUser.AccessToken, cancellationToken);
        return NoContent();
    }
}

public sealed record RegisterRequest(
    [Required, StringLength(254)] string Email,
    [Required, StringLength(128, MinimumLength = 12)] string Password,
    [Required, StringLength(100)] string DisplayName,
    [Required, StringLength(43, MinimumLength = 43)] string CodeChallenge);
public sealed record LoginRequest([Required, StringLength(254)] string Email, [Required, StringLength(128)] string Password);
public sealed record RefreshRequest([Required, StringLength(4096)] string RefreshToken);
public sealed record ExchangeRequest([Required, StringLength(4096)] string Code, [Required, StringLength(128, MinimumLength = 43)] string CodeVerifier);
public sealed record RecoveryRequest([Required, StringLength(254)] string Email, [Required, StringLength(43, MinimumLength = 43)] string CodeChallenge);
public sealed record PasswordRequest([Required, StringLength(128, MinimumLength = 12)] string NewPassword);
