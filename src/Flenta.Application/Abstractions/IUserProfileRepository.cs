using Flenta.Domain.Entities;

namespace Flenta.Application.Abstractions;

/// <summary>Access is always scoped to the owner of the supplied session.</summary>
public interface IUserProfileRepository
{
    Task<UserProfile> GetCurrentAsync(string accessToken, CancellationToken cancellationToken);
    Task<UserProfile> UpdateCurrentAsync(string accessToken, string displayName, CancellationToken cancellationToken);
}
