using Microsoft.Extensions.DependencyInjection;

namespace Flenta.Application;

public static class DependencyInjection
{
    /// <summary>Composition entry point for use cases as features are implemented.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // No registration or authentication use cases are implemented in the architecture stage.
        return services;
    }
}
