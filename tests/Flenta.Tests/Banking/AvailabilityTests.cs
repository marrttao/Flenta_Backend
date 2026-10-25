using System.Net;
using System.Net.Http.Headers;
using Flenta.Application.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flenta.Tests.Banking;

public sealed class AvailabilityTests
{
    [Theory]
    [InlineData("/api/accounts")]
    [InlineData("/api/cards")]
    [InlineData("/api/transactions")]
    public async Task Banking_endpoints_require_a_validated_identity(string route)
    {
        using var host = new WebApplicationFactory<Program>();
        using var client = host.CreateClient();
        using var response = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Disabled_banking_returns_503_without_attempting_a_database_connection()
    {
        using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IUserProfileRepository>();
            services.AddSingleton<IUserProfileRepository, TestProfiles>();
        }));
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "alice");
        using var response = await client.GetAsync("/api/accounts");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("banking.not_configured", await response.Content.ReadAsStringAsync());
    }
}
