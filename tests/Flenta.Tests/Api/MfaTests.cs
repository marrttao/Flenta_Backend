using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Flenta.Application.Abstractions.Kyc;
using Flenta.Application.Features.Auth;
using Flenta.Infrastructure.Configuration;
using Flenta.Tests.Kyc;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
namespace Flenta.Tests.Api;

public sealed class MfaTests
{
    private static readonly Guid Owner = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid Factor = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static string Token(string aal) => "fixture." + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{{\"sub\":\"{Owner}\",\"aal\":\"{aal}\"}}")).TrimEnd('=').Replace('+', '-').Replace('/', '_') + ".fixture";
    private static object Profile(object? factors = null) => new
    {
        id = Owner,
        email = "test@example.com",
        email_confirmed_at = "2026-11-01T00:00:00Z",
        app_metadata = new { role = "admin" },
        user_metadata = new { display_name = "MFA Test" },
        factors = factors ?? new[] { new { id = Factor, factor_type = "totp", friendly_name = "Authenticator", status = "verified" } }
    };
    private static WebApplicationFactory<Program> Host(MfaProvider handler) => new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.ConfigureServices(s =>
    {
        s.AddSingleton<IKycAccessPolicy, ApprovedKycPolicy>();
        s.Configure<SupabaseOptions>(o => { o.Enabled = true; o.Url = "http://127.0.0.1:9999"; o.PublishableKey = "sb_publishable_fixture"; });
        s.AddHttpClient("SupabaseAuthClient").ConfigurePrimaryHttpMessageHandler(() => handler);
    }));
    [Fact]
    public async Task A_verified_factor_requires_AAL2_and_verification_returns_the_upgraded_session()
    {
        using var provider = new MfaProvider(); using var host = Host(provider); using var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token("aal1"));
        using var denied = await client.GetAsync("/api/admin/access");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode); Assert.Contains("auth.mfa_required", await denied.Content.ReadAsStringAsync());
        var profile = await client.GetFromJsonAsync<ProfileResponse>("/api/profile"); Assert.True(profile!.MfaEnabled); Assert.False(profile.MfaVerified);
        using var verify = await client.PostAsJsonAsync("/api/auth/mfa/verify", new { factorId = Factor, code = "123456" });
        verify.EnsureSuccessStatusCode(); var session = (await verify.Content.ReadFromJsonAsync<AuthSession>())!;
        Assert.True(session.User.MfaVerified); Assert.Equal(Token("aal2"), session.AccessToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        (await client.GetAsync("/api/admin/access")).EnsureSuccessStatusCode();
        Assert.Equal(1, provider.Challenges); Assert.Equal("no-store", verify.Headers.CacheControl?.ToString());
    }
    [Fact]
    public async Task A_forged_AAL2_token_is_rejected_by_fresh_provider_validation()
    {
        using var provider = new MfaProvider(); using var host = Host(provider); using var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token("aal2") + "tampered");
        using var denied = await client.GetAsync("/api/admin/access"); Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
    }
    [Fact]
    public async Task Unenrollment_and_session_revocation_cannot_bypass_second_factor()
    {
        using var provider = new MfaProvider(); using var host = Host(provider); using var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token("aal1"));
        using var remove = await client.DeleteAsync("/api/auth/mfa/" + Factor); Assert.Equal(HttpStatusCode.Forbidden, remove.StatusCode);
        using var sessions = await client.PostAsync("/api/auth/sessions/sign-out-others", null); Assert.Equal(HttpStatusCode.Forbidden, sessions.StatusCode);
        Assert.Equal(0, provider.Removals);
    }
    [Fact]
    public async Task Corrupt_provider_factors_fail_closed()
    {
        using var provider = new MfaProvider { Corrupt = true }; using var host = Host(provider); using var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token("aal2"));
        using var denied = await client.GetAsync("/api/admin/access"); Assert.Equal(HttpStatusCode.ServiceUnavailable, denied.StatusCode);
    }
    private sealed class MfaProvider : HttpMessageHandler
    {
        public int Challenges { get; private set; }
        public int Removals { get; private set; }
        public bool Corrupt { get; init; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var token = request.Headers.Authorization?.Parameter;
            if (token != Token("aal1") && token != Token("aal2")) return new(HttpStatusCode.Unauthorized) { Content = JsonContent.Create(new { error_code = "invalid_jwt" }) };
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/user")) return new(HttpStatusCode.OK) { Content = JsonContent.Create(Profile(Corrupt ? "invalid" : null)) };
            if (path.EndsWith("/challenge")) { Challenges++; return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { id = Guid.Parse("30000000-0000-0000-0000-000000000001") }) }; }
            if (path.EndsWith("/verify"))
            {
                var body = await request.Content!.ReadFromJsonAsync<System.Text.Json.JsonElement>(ct);
                if (body.GetProperty("code").GetString() != "123456") return new(HttpStatusCode.BadRequest) { Content = JsonContent.Create(new { error_code = "mfa_verification_failed" }) };
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { access_token = Token("aal2"), refresh_token = "fixture-refresh", expires_in = 3600, user = Profile() }) };
            }
            if (request.Method == HttpMethod.Delete) { Removals++; return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { id = Factor }) }; }
            throw new InvalidOperationException("Unexpected provider call.");
        }
    }
}
