using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flenta.Api.Controllers;
using Flenta.Application.Abstractions;
using Flenta.Domain.Exceptions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Flenta.Tests.Api;

public sealed class ApiTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Health_endpoint_reports_a_running_host()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Status_endpoint_uses_the_injected_application_clock()
    {
        var timestamp = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddSingleton<IClock>(new FixedClock(timestamp))));
        using var client = host.CreateClient();

        var status = await client.GetFromJsonAsync<ApiStatusResponse>("/api/status");

        Assert.NotNull(status);
        Assert.Equal("Flenta API", status.Service);
        Assert.Equal("ok", status.Status);
        Assert.Equal(timestamp, status.Timestamp);
    }

    [Fact]
    public async Task Development_openapi_document_describes_the_status_endpoint()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.True(document.RootElement.GetProperty("paths").TryGetProperty("/api/status", out _));
    }

    [Fact]
    public async Task Openapi_document_is_not_exposed_in_production()
    {
        using var host = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        using var client = host.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("domain", HttpStatusCode.BadRequest)]
    [InlineData("unexpected", HttpStatusCode.InternalServerError)]
    public async Task Errors_use_problem_details_and_do_not_disclose_internal_messages(
        string failure, HttpStatusCode expectedStatus)
    {
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddControllers().AddApplicationPart(typeof(FailureController).Assembly)));
        using var client = host.CreateClient();
        using var response = await client.GetAsync($"/_tests/failures/{failure}");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("private-diagnostic-marker", body);
        using var document = JsonDocument.Parse(body);
        Assert.Equal((int)expectedStatus, document.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("traceId").GetString()));

        if (failure == "domain")
        {
            Assert.Equal("email.invalid", document.RootElement.GetProperty("code").GetString());
        }
    }

    private sealed class FixedClock(DateTimeOffset timestamp) : IClock
    {
        public DateTimeOffset UtcNow => timestamp;
    }
}

// Loaded only into the test host, never into the production API.
[ApiController]
[Microsoft.AspNetCore.Authorization.AllowAnonymous]
[Route("_tests/failures")]
public sealed class FailureController : ControllerBase
{
    [HttpGet("domain")]
    public IActionResult DomainFailure()
    {
        throw new DomainValidationException("email.invalid", "A valid email address is required.");
    }

    [HttpGet("unexpected")]
    public IActionResult UnexpectedFailure()
    {
        throw new InvalidOperationException("private-diagnostic-marker");
    }
}
