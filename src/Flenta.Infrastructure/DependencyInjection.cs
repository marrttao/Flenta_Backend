using Flenta.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Flenta.Infrastructure.Authentication;
using Flenta.Infrastructure.Configuration;
using Flenta.Infrastructure.Persistence;

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
        return services;
    }
}
