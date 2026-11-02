using Flenta.Application.Abstractions;
using Flenta.Domain.Entities;
using Flenta.Infrastructure.Authentication;

namespace Flenta.Infrastructure.Persistence;

internal sealed class SupabaseUserProfileRepository(SupabaseAuthClient client) : IUserProfileRepository
{
    public async Task<UserProfile> GetCurrentAsync(string accessToken, CancellationToken cancellationToken) =>
        SupabaseAuthClient.ReadUser(await client.SendAsync(HttpMethod.Get, "user", null, accessToken, cancellationToken));

    public async Task<UserProfile> UpdateCurrentAsync(string accessToken, string displayName, CancellationToken cancellationToken) =>
        SupabaseAuthClient.ReadUser(await client.SendAsync(HttpMethod.Put, "user", new { data = new { display_name = displayName } }, accessToken, cancellationToken));
}
