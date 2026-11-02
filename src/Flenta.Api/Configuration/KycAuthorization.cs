using Flenta.Application.Abstractions.Kyc;
using Microsoft.AspNetCore.Authorization;

namespace Flenta.Api.Configuration;

public static class KycPolicies
{
    public const string Onboarding = "Onboarding";
}

internal sealed class VerifiedKycRequirement : IAuthorizationRequirement;

internal sealed class VerifiedKycHandler(IKycAccessPolicy policy, IHttpContextAccessor accessor) : AuthorizationHandler<VerifiedKycRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, VerifiedKycRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true) return;
        await policy.RequireVerifiedAsync(accessor.HttpContext?.RequestAborted ?? default);
        context.Succeed(requirement);
    }
}
