using Flenta.Application.Abstractions.Kyc;

namespace Flenta.Tests.Kyc;

// Existing auth/banking fixtures exercise users who have already completed onboarding.
internal sealed class ApprovedKycPolicy : IKycAccessPolicy
{
    public Task RequireVerifiedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
