using Microsoft.Extensions.DependencyInjection;
using Flenta.Application.Features.Auth;
using Flenta.Application.Features.Profile;

namespace Flenta.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<AuthenticationService>();
        services.AddScoped<ProfileService>();
        return services;
    }
}
