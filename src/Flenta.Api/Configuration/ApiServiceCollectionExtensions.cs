using System.Diagnostics;
using Flenta.Api.Middleware;
using Flenta.Application.Abstractions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

namespace Flenta.Api.Configuration;

public static class ApiServiceCollectionExtensions
{
    public static IServiceCollection AddApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddControllers();
        services.AddOpenApi();
        services.AddHealthChecks();
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        services.AddAuthentication("Supabase")
            .AddScheme<AuthenticationSchemeOptions, SupabaseAuthenticationHandler>("Supabase", _ => { });
        services.AddAuthorization();
        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        services.AddCors(options => options.AddDefaultPolicy(policy =>
        {
            if (origins.Length > 0) policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
        }));
        services.AddRateLimiter(options =>
        {
            options.AddPolicy("banking", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 120,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true
                }));
            options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true
                }));
            options.OnRejected = async (context, _) =>
            {
                context.HttpContext.Response.Headers.RetryAfter = "60";
                await Results.Problem(statusCode: StatusCodes.Status429TooManyRequests, title: "Too many attempts. Try again later.",
                    extensions: new Dictionary<string, object?> { ["code"] = "auth.rate_limited" }).ExecuteAsync(context.HttpContext);
            };
        });
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = context =>
                context.ProblemDetails.Extensions["traceId"] =
                    Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
        });

        return services;
    }
}
