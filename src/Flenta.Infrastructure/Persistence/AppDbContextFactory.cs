using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Flenta.Infrastructure.Persistence;

public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var root = FindRepositoryRoot();
        var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
        var apiDirectory = Path.Combine(root, "src", "Flenta.Api");
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.Exists(apiDirectory) ? apiDirectory : root)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddInMemoryCollection(ReadEnvironmentFile(Path.Combine(root, ".env")))
            .AddInMemoryCollection(ReadEnvironmentFile(Path.Combine(root, ".env.local")))
            .AddEnvironmentVariables()
            .Build();
        var connection = DatabaseConfiguration.ResolveConnectionString(configuration)
            ?? throw new InvalidOperationException(
                "Set ConnectionStrings__AppDb in the environment or Backend .env.local. " +
                "DB_CONNECTION_STRING and Banking__ConnectionString are also supported.");
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options);
    }

    private static string FindRepositoryRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Flenta.Backend.sln"))) return directory.FullName;
                directory = directory.Parent;
            }
        }
        // A migration bundle can run outside the source checkout with environment configuration.
        return Directory.GetCurrentDirectory();
    }

    private static IEnumerable<KeyValuePair<string, string?>> ReadEnvironmentFile(string path)
    {
        if (!File.Exists(path)) yield break;
        foreach (var line in File.ReadLines(path))
        {
            var text = line.Trim();
            if (text.Length == 0 || text.StartsWith('#')) continue;
            if (text.StartsWith("export ", StringComparison.Ordinal)) text = text[7..].TrimStart();
            var separator = text.IndexOf('=');
            if (separator <= 0) continue;
            var key = text[..separator].Trim().Replace("__", ConfigurationPath.KeyDelimiter);
            var value = text[(separator + 1)..].Trim();
            if (value.Length >= 2 && (value[0] == (char)34 && value[^1] == (char)34 || value[0] == (char)39 && value[^1] == (char)39))
                value = value[1..^1];
            yield return new KeyValuePair<string, string?>(key, value);
        }
    }
}
