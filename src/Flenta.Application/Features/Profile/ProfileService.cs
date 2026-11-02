using Flenta.Application.Abstractions;
using Flenta.Application.Features.Auth;
using Flenta.Domain.Entities;

namespace Flenta.Application.Features.Profile;

public sealed class ProfileService(IUserProfileRepository repository, ICurrentUser currentUser)
{
    public ProfileResponse GetCurrent() => ToResponse(currentUser.Profile);

    public async Task<ProfileResponse> UpdateCurrentAsync(string displayName, CancellationToken cancellationToken)
    {
        var profile = currentUser.Profile;
        profile.Rename(displayName);
        var updated = await repository.UpdateCurrentAsync(currentUser.AccessToken, profile.DisplayName, cancellationToken);
        return ToResponse(updated);
    }

    public static ProfileResponse ToResponse(UserProfile profile) =>
        new(profile.Id, profile.Email.Value, profile.DisplayName, profile.EmailConfirmed);
}
