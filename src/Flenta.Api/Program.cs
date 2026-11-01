using Flenta.Api.Configuration;
using Flenta.Application;
using Flenta.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddApplication()
    .AddInfrastructure()
    .AddApi(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/auth") || context.Request.Path.StartsWithSegments("/api/profile")
        || context.Request.Path.StartsWithSegments("/api/accounts") || context.Request.Path.StartsWithSegments("/api/cards")
        || context.Request.Path.StartsWithSegments("/api/transactions") || context.Request.Path.StartsWithSegments("/api/goals")
        || context.Request.Path.StartsWithSegments("/api/finance"))
        context.Response.Headers.CacheControl = "no-store";
    await next(context);
});
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

// Exposes the composition root to the integration-test host.
public partial class Program;
