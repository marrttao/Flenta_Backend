using Flenta.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Flenta.Infrastructure.Authentication;
using Flenta.Infrastructure.Configuration;
using Flenta.Infrastructure.Persistence;
using Flenta.Application.Abstractions.Banking;
using Flenta.Application.Features.Transactions;
using Flenta.Domain.ValueObjects;
using Flenta.Infrastructure.Persistence.Banking;
using Flenta.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;

namespace Flenta.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.TryAddSingleton<IClock, SystemClock>();
        services.AddOptions<SupabaseOptions>()
            .BindConfiguration("Supabase")
            .Validate(options => !options.Enabled || SupabaseOptions.IsSafeUrl(options.Url)
                && SupabaseOptions.IsSafeUrl(options.FrontendUrl)
                && SupabaseOptions.IsPublicKey(options.PublishableKey),
                "Configure safe Supabase and frontend URLs and a public publishable/anon key.")
            .ValidateOnStart();
        services.AddHttpClient<SupabaseAuthClient>(client => client.Timeout = TimeSpan.FromSeconds(15))
            .RemoveAllLoggers();
        services.AddScoped<IIdentityProvider, SupabaseIdentityProvider>();
        services.AddScoped<IUserProfileRepository, SupabaseUserProfileRepository>();
        services.AddOptions<BankingOptions>().BindConfiguration("Banking")
            .Configure<IConfiguration>((options, configuration) =>
                options.ConnectionString = DatabaseConfiguration.ResolveConnectionString(configuration, options.ConnectionString) ?? "")
            .Validate(options => options.IbanFeeRate is >= 0 and <= 1
                && options.MinimumIbanFeeMinorUnits is >= 0 and <= Money.MaximumMinorUnits,
                "Banking fee configuration is invalid.")
            .Validate(options => !options.Enabled || !string.IsNullOrWhiteSpace(options.ConnectionString)
                && BankingOptions.ValidKey(options.CardEncryptionKey),
                "Enabled banking requires a PostgreSQL connection and a base64-encoded 32-byte card encryption key.")
            .ValidateOnStart();
        services.AddDbContext<AppDbContext>((provider, builder) =>
        {
            var connection = provider.GetRequiredService<IOptions<BankingOptions>>().Value.ConnectionString;
            builder.UseNpgsql(string.IsNullOrWhiteSpace(connection) ? null : connection);
        });
        services.AddScoped<PostgresBankingStore>();
        services.AddScoped<IBankingRepository>(provider => provider.GetRequiredService<PostgresBankingStore>());
        services.AddScoped<IBankingUnitOfWork>(provider => provider.GetRequiredService<PostgresBankingStore>());
        services.AddSingleton<ICardVault, SandboxCardVault>();
        services.AddSingleton<IFeePolicy>(provider =>
        {
            var settings = provider.GetRequiredService<IOptions<BankingOptions>>().Value;
            return new SandboxFeePolicy(settings.IbanFeeRate, Money.FromMinorUnits(settings.MinimumIbanFeeMinorUnits));
        });
        return services;
    }
}
