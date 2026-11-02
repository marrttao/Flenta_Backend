using Flenta.Api.Configuration;
using Flenta.Application;
using Flenta.Infrastructure;
using Flenta.Infrastructure.Kyc;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddApplication()
    .AddInfrastructure()
    .AddApi(builder.Configuration);

builder.Services.AddOptions<DiditOptions>()
    .Validate(options => !options.Enabled || !options.Sandbox || !builder.Environment.IsProduction(),
        "Didit sandbox cannot be enabled in Production.");

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/auth") || context.Request.Path.StartsWithSegments("/api/profile")
        || context.Request.Path.StartsWithSegments("/api/accounts") || context.Request.Path.StartsWithSegments("/api/cards")
        || context.Request.Path.StartsWithSegments("/api/transactions") || context.Request.Path.StartsWithSegments("/api/goals")
        || context.Request.Path.StartsWithSegments("/api/finance") || context.Request.Path.StartsWithSegments("/api/kyc"))
        context.Response.Headers.CacheControl = "no-store";
    await next(context);
});
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.MapControllers();
app.MapHealthChecks("/health").AllowAnonymous();
app.MapFallback("{**path}", () => Results.NotFound()).AllowAnonymous();

app.Run();

// Exposes the composition root to the integration-test host.
public partial class Program;
