using System.ComponentModel.DataAnnotations;
using Flenta.Api.Configuration;
using Flenta.Application.Abstractions;
using Flenta.Application.Features.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
namespace Flenta.Api.Controllers;

[ApiController, Authorize(Policy = KycPolicies.Onboarding), Route("api/auth/mfa"), EnableRateLimiting("auth"), RequestSizeLimit(4096)]
public sealed class MfaController(ISecondFactorProvider provider, ICurrentUser user) : ControllerBase
{
    [HttpGet] public Task<IReadOnlyList<MfaFactor>> List(CancellationToken ct) => provider.FactorsAsync(user.AccessToken, ct);
    [HttpPost("enroll")]
    public Task<MfaEnrollment> Enroll(CancellationToken ct)
    {
        RequireCurrentAssurance(); return provider.EnrollAsync(user.AccessToken, ct);
    }
    [HttpPost("verify")]
    public async Task<AuthSession> Verify(MfaCodeRequest request, CancellationToken ct)
    {
        var session = await provider.VerifyAsync(user.AccessToken, request.FactorId, request.Code, ct);
        if (session.User.Id != user.Profile.Id || !session.User.MfaVerified) throw new ApplicationFailureException(FailureKind.Unavailable, "auth.invalid_mfa_session", "Authenticator confirmation failed.");
        return session;
    }
    [HttpDelete("{id:guid}")]
    public Task Remove(Guid id, CancellationToken ct)
    {
        RequireCurrentAssurance(); return provider.RemoveAsync(user.AccessToken, id, ct);
    }
    private void RequireCurrentAssurance()
    {
        if (user.Profile.MfaEnabled && !user.Profile.MfaVerified) throw new ApplicationFailureException(FailureKind.Forbidden, "auth.mfa_required", "Confirm your authenticator code first.");
    }
}
public sealed record MfaCodeRequest(Guid FactorId, [Required, RegularExpression("^[0-9]{6}$")] string Code);
[ApiController, Authorize(Policy = KycPolicies.Onboarding), Route("api/auth/sessions"), EnableRateLimiting("auth")]
public sealed class SessionsController(ISecondFactorProvider provider, ICurrentUser user) : ControllerBase
{
    [HttpPost("sign-out-others")] public Task Others(CancellationToken ct) => provider.SignOutOthersAsync(user.AccessToken, ct);
}
