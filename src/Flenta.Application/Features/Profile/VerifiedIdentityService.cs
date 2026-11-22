using Flenta.Application.Abstractions;
using Flenta.Application.Abstractions.Kyc;
using Flenta.Application.Features.Auth;
namespace Flenta.Application.Features.Profile;

public sealed class VerifiedIdentityService(IKycRepository repository, IKycProvider provider, IIdentityVault vault, ICurrentUser user, IKycAccessPolicy access)
{
    public async Task<KycIdentityData?> GetAsync(CancellationToken ct)
    {
        await access.RequireVerifiedAsync(ct);
        var config = provider.Configuration;
        var verification = await repository.GetAsync(user.Profile.Id, config.Name, false, ct);
        if (verification is null || !verification.Matches(config.Name, config.ConfigurationId, config.Sandbox)) return null;
        var snapshot = await repository.LatestIdentityAsync(verification.Id, ct);
        return snapshot is null ? null : vault.Open(verification.Id, snapshot.Ciphertext);
    }
}
