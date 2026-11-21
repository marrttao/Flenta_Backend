using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Flenta.Application.Abstractions.Admin;
using Flenta.Application.Features.Admin;
using Flenta.Infrastructure.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flenta.Tests.Admin;

public sealed class AdminApiTests
{
    [Theory]
    [InlineData("/api/admin/access")]
    [InlineData("/api/admin/overview")]
    [InlineData("/api/admin/clients")]
    public async Task Anonymous_requests_cannot_read_administrative_data(string path)
    {
        var repository = new Repository();
        using var host = Host(new Provider(), repository);
        using var client = host.CreateClient();
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, repository.Reads);
    }

    [Theory]
    [InlineData("user")]
    [InlineData("unconfirmed")]
    [InlineData("invalid-confirmation")]
    [InlineData("malformed-role")]
    public async Task Only_a_confirmed_server_managed_admin_role_is_accepted(string token)
    {
        var repository = new Repository();
        using var host = Host(new Provider(), repository);
        using var client = Client(host, token);
        client.DefaultRequestHeaders.Add("X-Admin", "true");
        foreach (var path in new[] { "access", "overview", "clients" })
        {
            using var response = await client.GetAsync("/api/admin/" + path);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        Assert.Equal(0, repository.Reads);
    }

    [Fact]
    public async Task Admin_reads_work_without_customer_KYC_and_are_not_cached()
    {
        var repository = new Repository();
        using var host = Host(new Provider(), repository);
        using var client = Client(host, "admin");
        using var access = await client.GetAsync("/api/admin/access");
        access.EnsureSuccessStatusCode();
        using var overview = await client.GetAsync("/api/admin/overview");
        overview.EnsureSuccessStatusCode();
        Assert.Equal("no-store", overview.Headers.CacheControl?.ToString());
        var data = await overview.Content.ReadFromJsonAsync<AdminOverviewResponse>();
        Assert.Equal(2, data?.TotalClients);
        Assert.Equal(1, repository.Reads);
    }

    [Fact]
    public async Task Role_revocation_takes_effect_on_the_next_request()
    {
        var provider = new Provider();
        var repository = new Repository();
        using var host = Host(provider, repository);
        using var client = Client(host, "admin");
        using var allowed = await client.GetAsync("/api/admin/overview");
        allowed.EnsureSuccessStatusCode();
        provider.Revoked = true;
        using var denied = await client.GetAsync("/api/admin/overview");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(1, repository.Reads);
    }

    [Fact]
    public async Task Forged_tokens_are_rejected_by_the_identity_provider()
    {
        var repository = new Repository();
        using var host = Host(new Provider(), repository);
        using var client = Client(host, "forged-admin");
        using var denied = await client.GetAsync("/api/admin/clients");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        Assert.Equal(0, repository.Reads);
    }

    [Theory]
    [InlineData("?page=0")]
    [InlineData("?pageSize=101")]
    [InlineData("?pageSize=0")]
    [InlineData("?email=a%0Ab")]
    public async Task Invalid_client_filters_never_reach_storage(string query)
    {
        var repository = new Repository();
        using var host = Host(new Provider(), repository);
        using var client = Client(host, "admin");
        using var response = await client.GetAsync("/api/admin/clients" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, repository.Reads);
    }

    [Fact]
    public async Task Search_and_pagination_are_forwarded_without_exposing_metadata()
    {
        var repository = new Repository();
        using var host = Host(new Provider(), repository);
        using var client = Client(host, "admin");
        using var response = await client.GetAsync("/api/admin/clients?email=%20alex%40example.com%20&page=2&pageSize=8");
        response.EnsureSuccessStatusCode();
        Assert.Equal(("alex@example.com", 2, 8), repository.LastQuery);
        Assert.DoesNotContain("user_metadata", await response.Content.ReadAsStringAsync());
    }

    private static WebApplicationFactory<Program> Host(Provider provider, Repository repository) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.Configure<SupabaseOptions>(options =>
            {
                options.Enabled = true;
                options.Url = "http://127.0.0.1:9999";
                options.PublishableKey = "sb_publishable_test_fixture";
            });
            services.AddHttpClient("SupabaseAuthClient").ConfigurePrimaryHttpMessageHandler(() => provider);
            services.RemoveAll<IAdminRepository>();
            services.AddSingleton<IAdminRepository>(repository);
        }));

    private static HttpClient Client(WebApplicationFactory<Program> host, string token)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private sealed class Provider : HttpMessageHandler
    {
        public bool Revoked { get; set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var token = request.Headers.Authorization?.Parameter;
            if (token == "forged-admin") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            object role = token == "malformed-role" ? true : token != "user" && !Revoked ? "admin" : "customer";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new
            {
                id = Guid.Parse("10000000-0000-0000-0000-000000000001"), email = "admin@example.com",
                email_confirmed_at = token == "unconfirmed" ? null : token == "invalid-confirmation" ? "" : "2026-11-01T00:00:00Z",
                app_metadata = new { role }, user_metadata = new { display_name = "Admin", role = "admin", isAdmin = true }
            }) });
        }
    }

    private sealed class Repository : IAdminRepository
    {
        public int Reads { get; private set; }
        public (string Email, int Page, int Size) LastQuery { get; private set; }
        public Task<AdminOverviewResponse> OverviewAsync(DateTimeOffset now, CancellationToken ct)
        {
            Reads++;
            return Task.FromResult(new AdminOverviewResponse(2, 1, 3, DateOnly.FromDateTime(now.UtcDateTime), now, []));
        }
        public Task<AdminClientsResponse> ClientsAsync(string email, int page, int pageSize, CancellationToken ct)
        {
            Reads++;
            LastQuery = (email, page, pageSize);
            return Task.FromResult(new AdminClientsResponse([], 0, 2, page, pageSize));
        }
    }
}
