using Microsoft.Extensions.Configuration;

namespace Flenta.Infrastructure.Persistence;

public static class DatabaseConfiguration
{
    public const string ConnectionName = "AppDb";

    // Keep existing installations working while adopting the standard .NET key.
    public static string? ResolveConnectionString(IConfiguration configuration, string? legacyConnectionString = null) =>
        new[]
        {
            configuration.GetConnectionString(ConnectionName),
            configuration["DB_CONNECTION_STRING"],
            legacyConnectionString,
            configuration["Banking:ConnectionString"]
        }.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
