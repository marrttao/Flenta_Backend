using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flenta.Application.Features.Auth;
using Flenta.Domain.Entities;
using Flenta.Infrastructure.Kyc;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Flenta.Tests.Kyc;

public sealed class DiditClientTests
{
    internal static DiditOptions Settings() => new()
    { Enabled = true, Sandbox = true, ApiKey = "api-fixture", WebhookSecret = "hook-fixture", WorkflowId = KycTestProvider.WorkflowId };

    [Fact]
    public async Task Session_is_created_for_the_server_user_and_read_back_before_returning_its_url()
    {
        var settings = Settings(); using var handler = new Handler(); using var http = new HttpClient(handler);
        var result = await new DiditClient(http, Options.Create(settings)).CreateSessionAsync(KycVerification.ExternalId(KycProfiles.Alice), default);
        Assert.Equal(KycTestProvider.SessionId, result.SessionId);
        Assert.StartsWith("https://verify.didit.me/session/", result.VerificationUrl);
        var creation = Assert.Single(handler.Requests, r => r.Method == HttpMethod.Post);
        Assert.Equal("/v3/session/", creation.Path);
        using var body = JsonDocument.Parse(creation.Body);
        Assert.Equal(KycVerification.ExternalId(KycProfiles.Alice), body.RootElement.GetProperty("vendor_data").GetString());
        Assert.Equal(KycTestProvider.WorkflowId, body.RootElement.GetProperty("workflow_id").GetString());
        Assert.Equal("UKR", body.RootElement.GetProperty("expected_details").GetProperty("id_country").GetString());
        Assert.False(body.RootElement.TryGetProperty("sandbox_scenario", out _));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Theory]
    [InlineData("Not Started", "InProgress")]
    [InlineData("In Progress", "InProgress")]
    [InlineData("In Review", "PendingReview")]
    [InlineData("Approved", "Verified")]
    [InlineData("Declined", "Rejected")]
    [InlineData("Resubmitted", "NeedsResubmission")]
    [InlineData("Expired", "Expired")]
    [InlineData("Kyc Expired", "Expired")]
    [InlineData("Abandoned", "Expired")]
    [InlineData("New Unknown Status", "PendingReview")]
    public async Task Session_decisions_have_conservative_status_mapping(string status, string expected)
    {
        using var handler = new Handler { Status = status }; using var http = new HttpClient(handler);
        var result = await new DiditClient(http, Options.Create(Settings())).GetSessionAsync(KycTestProvider.SessionId, default);
        Assert.Equal(expected, result.Status.ToString()); Assert.Equal(expected == "Verified", result.RequirementsMet);
    }

    [Theory]
    [InlineData("UKR", "ID_CARD", "Approved", "passive", "Approved", true)]
    [InlineData("UKR", "Passport", "Approved", "PASSIVE", "Approved", true)]
    [InlineData("UKR", "ID", "Approved", "PASSIVE", "Approved", true)]
    [InlineData("POL", "PASSPORT", "Approved", "PASSIVE", "Approved", false)]
    [InlineData("UKR", "DRIVERS", "Approved", "PASSIVE", "Approved", false)]
    [InlineData("UKR", "ID_CARD", "Declined", "PASSIVE", "Approved", false)]
    [InlineData("UKR", "ID_CARD", "Approved", "ACTIVE_3D", "Approved", false)]
    [InlineData("UKR", "ID_CARD", "Approved", "PASSIVE", "Declined", false)]
    public async Task Approval_requires_Ukrainian_identity_passive_liveness_and_face_match(string country, string document, string liveness, string method, string face, bool expected)
    {
        using var handler = new Handler { Country = country, DocumentType = document, Liveness = liveness, Method = method, Face = face };
        using var http = new HttpClient(handler);
        var result = await new DiditClient(http, Options.Create(Settings())).GetSessionAsync(KycTestProvider.SessionId, default);
        Assert.Equal(expected, result.RequirementsMet);
    }

    [Theory]
    [InlineData("id_verifications")]
    [InlineData("liveness_checks")]
    [InlineData("face_matches")]
    public async Task Missing_feature_results_cannot_pass_kyc(string missing)
    {
        using var handler = new Handler { Missing = missing }; using var http = new HttpClient(handler);
        var result = await new DiditClient(http, Options.Create(Settings())).GetSessionAsync(KycTestProvider.SessionId, default);
        Assert.False(result.RequirementsMet);
    }

    [Theory]
    [InlineData("workflow")]
    [InlineData("environment")]
    [InlineData("session")]
    [InlineData("vendor")]
    public async Task Session_identity_is_bound_to_workflow_environment_and_owner(string changed)
    {
        using var handler = new Handler { Changed = changed }; using var http = new HttpClient(handler);
        var client = new DiditClient(http, Options.Create(Settings()));
        await Assert.ThrowsAsync<ApplicationFailureException>(() => client.CreateSessionAsync(KycVerification.ExternalId(KycProfiles.Alice), default));
    }

    [Theory]
    [InlineData("http://verify.didit.me/session/token")]
    [InlineData("https://verify.didit.me.attacker.invalid/session/token")]
    [InlineData("https://user:pass@verify.didit.me/session/token")]
    [InlineData("https://verify.didit.me:8443/session/token")]
    [InlineData("https://verify.didit.me/u/unbound-workflow")]
    [InlineData("https://verify.didit.me/session/token#unexpected")]
    public void Verification_URL_must_be_an_owned_provider_session_link(string url) =>
        Assert.Throws<ApplicationFailureException>(() => DiditClient.VerificationUrl(url));

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TemporaryRedirect)]
    public async Task Errors_and_redirects_do_not_leak_details_or_create_retries(HttpStatusCode failure)
    {
        using var handler = new Handler { Failure = failure }; using var http = new HttpClient(handler);
        var exception = await Assert.ThrowsAsync<ApplicationFailureException>(() => new DiditClient(http, Options.Create(Settings())).CreateSessionAsync("flenta-fixture", default));
        Assert.Equal(FailureKind.Unavailable, exception.Kind); Assert.DoesNotContain("private-provider-response", exception.Message);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public void Sandbox_configuration_is_rejected_in_production()
    {
        using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Production").ConfigureServices(services =>
            services.Configure<DiditOptions>(o => { o.Enabled = true; o.Sandbox = true; o.ApiKey = "fixture"; o.WebhookSecret = "fixture"; o.WorkflowId = KycTestProvider.WorkflowId; })));
        var error = Assert.Throws<OptionsValidationException>(() => host.CreateClient()); Assert.Contains("sandbox cannot be enabled", error.Message);
    }

    private sealed class Handler : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path, byte[] Body)> Requests { get; } = [];
        public string Status { get; init; } = "Approved";
        public string Country { get; init; } = "UKR";
        public string DocumentType { get; init; } = "ID_CARD";
        public string Liveness { get; init; } = "Approved";
        public string Method { get; init; } = "PASSIVE";
        public string Face { get; init; } = "Approved";
        public string? Missing { get; init; }
        public string? Changed { get; init; }
        public HttpStatusCode? Failure { get; init; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("https://verification.didit.me", request.RequestUri!.GetLeftPart(UriPartial.Authority));
            Assert.Equal("api-fixture", Assert.Single(request.Headers.GetValues("X-Api-Key")));
            Requests.Add((request.Method, request.RequestUri.PathAndQuery, request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(ct)));
            if (Failure is not null) return new HttpResponseMessage(Failure.Value) { Content = new StringContent("private-provider-response") };
            var decision = new Dictionary<string, object?>
            {
                ["session_id"] = Changed == "session" ? Guid.NewGuid().ToString("D") : KycTestProvider.SessionId,
                ["workflow_id"] = Changed == "workflow" ? Guid.NewGuid().ToString("D") : KycTestProvider.WorkflowId,
                ["vendor_data"] = KycVerification.ExternalId(Changed == "vendor" ? KycProfiles.Bob : KycProfiles.Alice),
                ["environment"] = Changed == "environment" ? "live" : "sandbox", ["status"] = Status,
                ["session_url"] = "https://verify.didit.me/session/fixture",
                ["id_verifications"] = new[] { new { status = "Approved", issuing_state = Country, document_type = DocumentType } },
                ["liveness_checks"] = new[] { new { status = Liveness, method = Method } },
                ["face_matches"] = new[] { new { status = Face } }
            };
            if (Missing is not null) decision.Remove(Missing);
            object payload = request.Method == HttpMethod.Post ? new { session_id = KycTestProvider.SessionId, url = "https://verify.didit.me/session/fixture" } : decision;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(payload) };
        }
    }
}
