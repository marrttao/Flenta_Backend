using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Flenta.Application.Features.Auth;
using Flenta.Infrastructure.Configuration;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Flenta.Application.Abstractions.Kyc;
using Flenta.Tests.Kyc;

namespace Flenta.Tests.Api;

public sealed class AuthenticationTests
{
    [Fact]
    public async Task Profile_and_password_changes_require_authentication()
    {
        using var host = CreateHost(new ProviderHandler());
        using var client = host.CreateClient();
        using var profile = await client.GetAsync("/api/profile");
        using var password = await client.PutAsJsonAsync("/api/auth/password", new { newPassword = "a-valid-long-password" });
        Assert.Equal(HttpStatusCode.Unauthorized, profile.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, password.StatusCode);
    }

    [Fact]
    public async Task Registration_validates_input_before_contacting_the_provider()
    {
        var provider = new ProviderHandler();
        using var host = CreateHost(provider);
        using var client = host.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/auth/register", new { email = "test@example.com", password = "short", displayName = "Test", codeChallenge = new string('a', 43) });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(provider.Requests);
    }

    [Fact]
    public async Task Signup_uses_PKCE_and_does_not_return_a_session_before_email_confirmation()
    {
        var provider = new ProviderHandler();
        using var host = CreateHost(provider);
        using var client = host.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/auth/register", new { email = "test@example.com", password = "a-valid-long-password", displayName = "Test", codeChallenge = new string('a', 43) });
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<RegistrationResult>();
        Assert.NotNull(result);
        Assert.True(result.RequiresEmailConfirmation);
        Assert.Null(result.Session);
        var request = Assert.Single(provider.Requests);
        Assert.Contains("redirect_to=", request.Path);
        using var body = JsonDocument.Parse(request.Body);
        Assert.Equal("s256", body.RootElement.GetProperty("code_challenge_method").GetString());
        Assert.Equal("Test", body.RootElement.GetProperty("data").GetProperty("display_name").GetString());
    }

    [Fact]
    public async Task Login_profile_update_password_and_logout_use_the_validated_user_session()
    {
        var provider = new ProviderHandler();
        using var host = CreateHost(provider);
        using var client = host.CreateClient();
        using var login = await client.PostAsJsonAsync("/api/auth/login", new { email = "test@example.com", password = "a-valid-long-password" });
        login.EnsureSuccessStatusCode();
        Assert.Equal("no-store", login.Headers.CacheControl?.ToString());
        var session = await login.Content.ReadFromJsonAsync<AuthSession>();
        Assert.NotNull(session);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        var profile = await client.GetFromJsonAsync<ProfileResponse>("/api/profile");
        Assert.Equal(ProviderHandler.UserId, profile?.Id);
        using var update = await client.PutAsJsonAsync("/api/profile", new { displayName = "New Name", userId = Guid.NewGuid() });
        update.EnsureSuccessStatusCode();
        var updated = await update.Content.ReadFromJsonAsync<ProfileResponse>();
        Assert.Equal("New Name", updated?.DisplayName);
        Assert.Equal(ProviderHandler.UserId, updated?.Id);
        using var password = await client.PutAsJsonAsync("/api/auth/password", new { newPassword = "new-valid-password" });
        Assert.Equal(HttpStatusCode.NoContent, password.StatusCode);
        using var logout = await client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Contains(provider.Requests, r => r.Path.EndsWith("logout?scope=local", StringComparison.Ordinal) && r.Token == "access-fixture");
    }

    [Fact]
    public async Task Refresh_and_PKCE_exchange_forward_the_expected_grants()
    {
        var provider = new ProviderHandler();
        using var host = CreateHost(provider);
        using var client = host.CreateClient();
        using var refresh = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = "refresh-fixture" });
        refresh.EnsureSuccessStatusCode();
        var session = await refresh.Content.ReadFromJsonAsync<AuthSession>();
        Assert.Equal("rotated-refresh-fixture", session?.RefreshToken);
        using var exchange = await client.PostAsJsonAsync("/api/auth/exchange", new { code = "one-time-code", codeVerifier = new string('b', 43) });
        exchange.EnsureSuccessStatusCode();
        Assert.Contains(provider.Requests, r => r.Path.EndsWith("grant_type=pkce", StringComparison.Ordinal) && r.Body.Contains("code_verifier", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Recovery_uses_PKCE_and_a_generic_non_enumerating_response()
    {
        var provider = new ProviderHandler();
        using var host = CreateHost(provider);
        using var client = host.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/auth/recover", new { email = "unknown@example.com", codeChallenge = new string('a', 43) });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Contains("If the address is registered", await response.Content.ReadAsStringAsync());
        Assert.Contains(provider.Requests, r => r.Path.Contains("recover?redirect_to=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Invalid_tokens_are_rejected_without_trusting_their_claims()
    {
        using var host = CreateHost(new ProviderHandler());
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "forged-token");
        using var response = await client.GetAsync("/api/profile");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Provider_network_errors_return_503_without_internal_details()
    {
        using var host = CreateHost(new ProviderHandler { Offline = true });
        using var client = host.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { email = "test@example.com", password = "a-valid-long-password" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain("private-provider-error", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Authentication_attempts_are_rate_limited()
    {
        using var host = CreateHost(new ProviderHandler());
        using var client = host.CreateClient();
        for (var i = 0; i < 10; i++)
        {
            using var response = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = "fixture" });
            response.EnsureSuccessStatusCode();
        }
        using var rejected = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = "fixture" });
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
    }

    private static WebApplicationFactory<Program> CreateHost(ProviderHandler provider) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddSingleton<IKycAccessPolicy, ApprovedKycPolicy>();
            services.Configure<SupabaseOptions>(options =>
            {
                options.Enabled = true;
                options.Url = "http://127.0.0.1:9999";
                options.PublishableKey = "sb_publishable_test_fixture";
            });
            services.AddHttpClient("SupabaseAuthClient").ConfigurePrimaryHttpMessageHandler(() => provider);
        }));

    private sealed class ProviderHandler : HttpMessageHandler
    {
        public static readonly Guid UserId = Guid.Parse("61b067c6-20e4-48bb-bfa6-bbecedbaeb99");
        public List<(string Path, string Body, string? Token)> Requests { get; } = [];
        public bool Offline { get; init; }
        private string displayName = "Test";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Offline) throw new HttpRequestException("private-provider-error");
            var path = request.RequestUri!.PathAndQuery;
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((path, body, request.Headers.Authorization?.Parameter));
            Assert.Equal("sb_publishable_test_fixture", Assert.Single(request.Headers.GetValues("apikey")));
            if (request.Headers.Authorization?.Parameter == "forged-token") return Json(HttpStatusCode.Unauthorized, new { error_code = "bad_jwt" });
            if (path.Contains("token?", StringComparison.Ordinal)) return Json(HttpStatusCode.OK, new
            {
                access_token = "access-fixture", refresh_token = "rotated-refresh-fixture", expires_in = 3600, user = User()
            });
            if (path.Contains("signup?", StringComparison.Ordinal)) return Json(HttpStatusCode.OK, User());
            if (path.EndsWith("/user", StringComparison.Ordinal))
            {
                if (request.Method == HttpMethod.Put)
                {
                    using var data = JsonDocument.Parse(body);
                    if (data.RootElement.TryGetProperty("data", out var metadata)) displayName = metadata.GetProperty("display_name").GetString()!;
                }
                return Json(HttpStatusCode.OK, User());
            }
            return Json(HttpStatusCode.OK, new { });
        }

        private object User() => new { id = UserId, email = "test@example.com", email_confirmed_at = "2026-01-01T00:00:00Z", user_metadata = new { display_name = displayName } };
        private static HttpResponseMessage Json(HttpStatusCode status, object value) => new(status) { Content = JsonContent.Create(value) };
    }
}
