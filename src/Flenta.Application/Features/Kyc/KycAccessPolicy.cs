using Flenta.Application.Abstractions;
using Flenta.Application.Abstractions.Kyc;
using Flenta.Application.Features.Auth;

namespace Flenta.Application.Features.Kyc;

public sealed class KycAccessPolicy(IKycRepository repository, IKycProvider provider, ICurrentUser user) : IKycAccessPolicy
{
    public async Task RequireVerifiedAsync(CancellationToken cancellationToken)
    {
        if (!user.Profile.EmailConfirmed)
            throw new ApplicationFailureException(FailureKind.Forbidden, "kyc.email_required", "Confirm your email before identity verification.");
        var configuration = provider.Configuration;
        var verification = await repository.GetAsync(user.Profile.Id, configuration.Name, false, cancellationToken);
        var manual = verification is not null && verification.Matches(configuration.Name, configuration.ConfigurationId, configuration.Sandbox)
            ? await repository.ManualDecisionAsync(verification, cancellationToken) : null;
        if (!(manual ?? verification?.CanAccess(configuration.Name, configuration.ConfigurationId, configuration.Sandbox) ?? false))
            throw new ApplicationFailureException(FailureKind.Forbidden, "kyc.required", "Complete identity verification to use the application.");
    }
}
