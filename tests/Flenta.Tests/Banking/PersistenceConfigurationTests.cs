using Flenta.Infrastructure.Persistence;
using Flenta.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Flenta.Tests.Banking;

public sealed class PersistenceConfigurationTests
{
    [Fact]
    public void Standard_connection_key_has_priority_over_infra_and_legacy_keys()
    {
        var configuration = Configuration(new()
        {
            ["ConnectionStrings:AppDb"] = "standard",
            ["DB_CONNECTION_STRING"] = "infra",
            ["Banking:ConnectionString"] = "legacy"
        });
        Assert.Equal("standard", DatabaseConfiguration.ResolveConnectionString(configuration, "options-override"));
    }

    [Theory]
    [InlineData("DB_CONNECTION_STRING")]
    [InlineData("Banking:ConnectionString")]
    public void Existing_connection_keys_remain_supported(string key)
    {
        Assert.Equal("existing", DatabaseConfiguration.ResolveConnectionString(Configuration(new() { [key] = "existing" })));
    }

    [Fact]
    public void Blank_standard_key_falls_back_to_legacy_options()
    {
        Assert.Equal("legacy", DatabaseConfiguration.ResolveConnectionString(Configuration(new() { ["ConnectionStrings:AppDb"] = " " }), "legacy"));
    }

    [Fact]
    public void Runtime_context_uses_the_same_standard_connection_key_as_the_factory()
    {
        const string connection = "Host=localhost;Database=runtime_configuration;Username=unused";
        var configuration = Configuration(new() { ["ConnectionStrings:AppDb"] = connection });
        using var services = new ServiceCollection().AddSingleton<IConfiguration>(configuration)
            .AddInfrastructure().BuildServiceProvider();
        using var scope = services.CreateScope();
        Assert.Equal(connection, scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.GetConnectionString());
    }

    [Fact]
    public void Renamed_context_discovers_the_existing_migration_without_schema_changes()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=model_check;Username=unused").Options);
        Assert.Equal("20261025110524_InitialBanking", Assert.Single(db.Database.GetMigrations()));
        Assert.False(db.Database.HasPendingModelChanges());
    }

    private static IConfiguration Configuration(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
}
