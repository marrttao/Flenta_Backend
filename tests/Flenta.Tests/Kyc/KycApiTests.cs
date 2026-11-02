using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Flenta.Application.Abstractions;
using Flenta.Application.Abstractions.Kyc;
using Flenta.Application.Features.Kyc;
using Flenta.Domain.Entities;
using Flenta.Infrastructure.Kyc;
using Flenta.Tests.Banking;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flenta.Tests.Kyc;

public sealed class KycApiTests
{
    internal const string WebhookSecret = "webhook-test-fixture";
    internal static WebApplicationFactory<Program> Host(KycMemoryStore store, KycTestProvider provider) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IUserProfileRepository>(); services.AddSingleton<IUserProfileRepository, KycProfiles>();
            services.RemoveAll<IKycProvider>(); services.AddSingleton<IKycProvider>(provider);
            services.RemoveAll<IKycRepository>(); services.AddSingleton<IKycRepository>(store);
            services.RemoveAll<IKycUnitOfWork>(); services.AddSingleton<IKycUnitOfWork>(store);
            services.RemoveAll<IIdentityProvider>(); services.AddSingleton<IIdentityProvider, TestIdentity>();
            services.Configure<DiditOptions>(o => { o.Enabled = true; o.ApiKey = "api-fixture"; o.WebhookSecret = WebhookSecret; o.WorkflowId = KycTestProvider.WorkflowId; });
            services.AddControllers().AddApplicationPart(typeof(UnguardedTestController).Assembly);
        }));
    internal static HttpClient Client(WebApplicationFactory<Program> host, string? user = "alice")
    {
        var client = host.CreateClient();
        if (user is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user);
        return client;
    }
    internal static byte[] Webhook(string eventId = "first", bool sandbox = true, string? sessionId = null) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        vendor_data = KycVerification.ExternalId(KycProfiles.Alice), session_id = sessionId ?? KycTestProvider.SessionId,
        workflow_id = KycTestProvider.WorkflowId, environment = sandbox ? "sandbox" : "live",
        webhook_type = "status.updated", event_id = eventId, status = "Approved", timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
    });
    internal static async Task<HttpResponseMessage> SendWebhook(HttpClient client, byte[] body, bool signed = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/kyc/webhooks/didit") { Content = new ByteArrayContent(body) };
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        try { using var json = JsonDocument.Parse(body); timestamp = json.RootElement.GetProperty("timestamp").GetInt64(); } catch (JsonException) { }
        request.Headers.Add("X-Timestamp", timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture));
        request.Headers.Add("X-Signature", signed ? Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(WebhookSecret), body)) : new string('0', 64));
        return await client.SendAsync(request);
    }

    [Theory]
    [InlineData("/api/profile")]
    [InlineData("/api/accounts")]
    [InlineData("/api/cards")]
    [InlineData("/api/transactions")]
    [InlineData("/api/goals")]
    [InlineData("/api/finance/rates")]
    [InlineData("/api/finance/cashback")]
    [InlineData("/api/finance/analytics")]
    [InlineData("/_tests/unguarded")]
    public async Task Every_application_area_is_closed_before_kyc(string path)
    {
        using var host = Host(new(), new()); using var client = Client(host);
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("kyc.required", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task New_users_can_start_only_their_own_verification_and_logout()
    {
        var store = new KycMemoryStore(); var provider = new KycTestProvider();
        using var host = Host(store, provider); using var client = Client(host);
        var initial = await client.GetFromJsonAsync<KycStatusResponse>("/api/kyc");
        Assert.Equal("NotStarted", initial!.Status); Assert.False(initial.CanAccess);
        using var token = await client.PostAsJsonAsync("/api/kyc/session", new { userId = KycProfiles.Bob, status = "Verified" });
        token.EnsureSuccessStatusCode();
        Assert.Equal("no-store", token.Headers.CacheControl!.ToString());
        Assert.Equal(KycVerification.ExternalId(KycProfiles.Alice), provider.LastExternalId);
        Assert.Equal(KycStatus.InProgress, Assert.Single(store.Verifications).Status);
        using var bob = Client(host, "bob");
        Assert.Equal("NotStarted", (await bob.GetFromJsonAsync<KycStatusResponse>("/api/kyc"))!.Status);
        using var logout = await client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
    }

    [Fact]
    public async Task Unconfirmed_email_and_anonymous_requests_cannot_start_kyc()
    {
        var store = new KycMemoryStore(); using var host = Host(store, new());
        using var anonymous = Client(host, null); using var unconfirmed = Client(host, "unconfirmed");
        using var noIdentity = await anonymous.PostAsync("/api/kyc/session", null);
        Assert.Equal(HttpStatusCode.Unauthorized, noIdentity.StatusCode);
        using var noEmail = await unconfirmed.PostAsync("/api/kyc/session", null);
        Assert.Equal(HttpStatusCode.Forbidden, noEmail.StatusCode); Assert.Empty(store.Verifications);
    }

    [Fact]
    public async Task Signed_webhook_is_only_a_trigger_for_authoritative_server_verification()
    {
        var store = new KycMemoryStore(); var provider = new KycTestProvider();
        using var host = Host(store, provider); using var client = Client(host); using var webhookClient = Client(host, null);
        using var start = await client.PostAsync("/api/kyc/session", null); start.EnsureSuccessStatusCode();
        using var pending = await SendWebhook(webhookClient, Webhook()); pending.EnsureSuccessStatusCode();
        Assert.Equal(KycStatus.PendingReview, store.Latest(KycProfiles.Alice)!.Status);
        using var blocked = await client.GetAsync("/api/profile"); Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        provider.Status = KycStatus.Verified;
        using var approved = await SendWebhook(webhookClient, Webhook("approved")); approved.EnsureSuccessStatusCode();
        using var profile = await client.GetAsync("/api/profile"); profile.EnsureSuccessStatusCode();
        Assert.True((await client.GetFromJsonAsync<KycStatusResponse>("/api/kyc"))!.CanAccess);
        var calls = provider.Requests;
        using var duplicate = await SendWebhook(webhookClient, Webhook("approved")); duplicate.EnsureSuccessStatusCode();
        Assert.Equal(calls, provider.Requests); Assert.Equal(2, store.Receipts.Count);
        provider.Status = KycStatus.Rejected;
        using var outdated = await SendWebhook(webhookClient, Webhook("old-delivered-late")); outdated.EnsureSuccessStatusCode();
        Assert.False((await client.GetFromJsonAsync<KycStatusResponse>("/api/kyc"))!.CanAccess);
        using var revoked = await client.GetAsync("/api/profile"); Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
    }

    [Fact]
    public async Task Invalid_signature_sandbox_mismatch_and_missing_document_checks_never_unlock_the_app()
    {
        var store = new KycMemoryStore(); var provider = new KycTestProvider { Status = KycStatus.Verified, RequirementsMet = false };
        using var host = Host(store, provider); using var client = Client(host); using var hooks = Client(host, null);
        using var start = await client.PostAsync("/api/kyc/session", null); start.EnsureSuccessStatusCode();
        using var forged = await SendWebhook(hooks, Webhook(), false); Assert.Equal(HttpStatusCode.Unauthorized, forged.StatusCode);
        Assert.Equal(0, provider.Requests);
        using var wrongMode = await SendWebhook(hooks, Webhook(sandbox: false)); wrongMode.EnsureSuccessStatusCode(); Assert.Equal(0, provider.Requests);
        using var incomplete = await SendWebhook(hooks, Webhook()); incomplete.EnsureSuccessStatusCode();
        var status = (await client.GetFromJsonAsync<KycStatusResponse>("/api/kyc"))!;
        Assert.False(status.CanAccess); Assert.Equal("NeedsResubmission", status.Status); Assert.Equal("kyc.requirements_not_met", status.FailureCode);
    }

    [Fact]
    public async Task Refresh_uses_provider_results_and_never_accepts_client_approval()
    {
        var store = new KycMemoryStore(); var provider = new KycTestProvider();
        using var host = Host(store, provider); using var client = Client(host);
        using var start = await client.PostAsync("/api/kyc/session", null); start.EnsureSuccessStatusCode();
        using var forged = await client.PostAsJsonAsync("/api/kyc/refresh", new { status = "Verified", requirementsMet = true }); forged.EnsureSuccessStatusCode();
        Assert.False((await forged.Content.ReadFromJsonAsync<KycStatusResponse>())!.CanAccess);
        provider.Status = KycStatus.Verified;
        using var refresh = await client.PostAsync("/api/kyc/refresh", null); refresh.EnsureSuccessStatusCode();
        Assert.True((await refresh.Content.ReadFromJsonAsync<KycStatusResponse>())!.CanAccess);
    }

    [Fact]
    public async Task Provider_failure_does_not_acknowledge_or_apply_a_webhook()
    {
        var store = new KycMemoryStore(); var provider = new KycTestProvider { Offline = true };
        using var host = Host(store, provider); using var client = Client(host); using var hooks = Client(host, null);
        using var start = await client.PostAsync("/api/kyc/session", null); start.EnsureSuccessStatusCode();
        using var failed = await SendWebhook(hooks, Webhook()); Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        Assert.Empty(store.Receipts); Assert.Equal(KycStatus.InProgress, store.Latest(KycProfiles.Alice)!.Status);
    }

    [Fact]
    public async Task Unconfigured_didit_fails_closed_without_breaking_health_or_login_routes()
    {
        using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        { services.RemoveAll<IUserProfileRepository>(); services.AddSingleton<IUserProfileRepository, KycProfiles>(); }));
        using var client = Client(host);
        using var profile = await client.GetAsync("/api/profile"); Assert.Equal(HttpStatusCode.ServiceUnavailable, profile.StatusCode);
        Assert.Contains("kyc.not_configured", await profile.Content.ReadAsStringAsync());
        using var health = await client.GetAsync("/health"); health.EnsureSuccessStatusCode();
        using var login = await client.PostAsJsonAsync("/api/auth/login", new { email = "invalid", password = "x" });
        Assert.Equal(HttpStatusCode.BadRequest, login.StatusCode);
    }

    [Fact]
    public async Task Malformed_and_oversized_webhooks_are_rejected_before_processing()
    {
        var store = new KycMemoryStore(); using var host = Host(store, new()); using var hooks = Client(host, null);
        using var invalid = await SendWebhook(hooks, Encoding.UTF8.GetBytes("not-json"));
        Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
        using var oversized = await SendWebhook(hooks, new byte[65537]);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode); Assert.Empty(store.Receipts);
    }

    [Fact]
    public async Task Reopening_and_concurrent_requests_reuse_the_owned_session()
    {
        var store = new KycMemoryStore(); var provider = new KycTestProvider { Status = KycStatus.InProgress };
        using var host = Host(store, provider); using var client = Client(host);
        var requests = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => client.PostAsync("/api/kyc/session", null)));
        foreach (var response in requests) { response.EnsureSuccessStatusCode(); response.Dispose(); }
        Assert.Equal(1, provider.Created); Assert.Single(store.Verifications);
        Assert.Equal(KycTestProvider.SessionId, Assert.Single(store.Verifications).ProviderSessionId);
    }

    [Fact]
    public async Task Expired_session_is_replaced_and_old_attempt_events_cannot_approve_the_new_one()
    {
        var store = new KycMemoryStore(); var provider = new KycTestProvider { Status = KycStatus.Expired };
        using var host = Host(store, provider); using var client = Client(host); using var hooks = Client(host, null);
        using var first = await client.PostAsync("/api/kyc/session", null); first.EnsureSuccessStatusCode();
        using var expired = await client.PostAsync("/api/kyc/refresh", null); expired.EnsureSuccessStatusCode();
        Assert.Equal("Expired", (await expired.Content.ReadFromJsonAsync<KycStatusResponse>())!.Status);
        using var next = await client.PostAsync("/api/kyc/session", null); next.EnsureSuccessStatusCode();
        Assert.Equal(2, provider.Created); Assert.Equal(2, store.Verifications.Count);
        var current = store.Latest(KycProfiles.Alice)!;
        Assert.Equal(2, current.AttemptNumber); Assert.NotEqual(KycTestProvider.SessionId, current.ProviderSessionId);
        provider.Status = KycStatus.Verified;
        using var late = await SendWebhook(hooks, Webhook("old-session-approved")); late.EnsureSuccessStatusCode();
        Assert.False((await client.GetFromJsonAsync<KycStatusResponse>("/api/kyc"))!.CanAccess);
        using var approved = await SendWebhook(hooks, Webhook("current-approved", sessionId: current.ProviderSessionId)); approved.EnsureSuccessStatusCode();
        Assert.True((await client.GetFromJsonAsync<KycStatusResponse>("/api/kyc"))!.CanAccess);
    }
}

[Microsoft.AspNetCore.Mvc.ApiController, Microsoft.AspNetCore.Mvc.Route("_tests/unguarded")]
public sealed class UnguardedTestController : Microsoft.AspNetCore.Mvc.ControllerBase
{
    [Microsoft.AspNetCore.Mvc.HttpGet]
    public string Get() => "Protected by fallback policy even without Authorize attributes.";
}
