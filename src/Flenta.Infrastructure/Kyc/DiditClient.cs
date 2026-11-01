using System.Net.Http.Headers;
using System.Text.Json;
using Flenta.Application.Abstractions.Kyc;
using Flenta.Application.Features.Auth;
using Flenta.Domain.Entities;
using Microsoft.Extensions.Options;

namespace Flenta.Infrastructure.Kyc;

public sealed class DiditClient(HttpClient client, IOptions<DiditOptions> options) : IKycProvider
{
    public KycProviderConfiguration Configuration
    {
        get
        {
            var settings = options.Value;
            if (!settings.Configured) throw new ApplicationFailureException(FailureKind.Unavailable, "kyc.not_configured", "Identity verification is temporarily unavailable.");
            return new KycProviderConfiguration("Didit", Guid.Parse(settings.WorkflowId).ToString("D"), settings.Sandbox);
        }
    }

    public async Task<KycSessionSnapshot> CreateSessionAsync(string externalUserId, CancellationToken ct)
    {
        var configuration = Configuration;
        using var created = await SendAsync(HttpMethod.Post, "/v3/session/", JsonSerializer.SerializeToUtf8Bytes(new
        {
            workflow_id = configuration.ConfigurationId, vendor_data = externalUserId, language = "uk",
            expected_details = new { id_country = "UKR" }
        }), ct);
        var id = SessionId(String(created.RootElement, "session_id"));
        var url = VerificationUrl(String(created.RootElement, "url")) ?? throw Unavailable();
        // Read back the server-side binding before returning the capability URL to the client.
        var session = await GetSessionAsync(id, ct);
        if (session.ExternalUserId != externalUserId) throw Unavailable();
        return session with { VerificationUrl = url };
    }

    public async Task<KycSessionSnapshot> GetSessionAsync(string sessionId, CancellationToken ct)
    {
        var configuration = Configuration;
        var id = SessionId(sessionId);
        using var json = await SendAsync(HttpMethod.Get, $"/v3/session/{id}/decision/", null, ct);
        var root = json.RootElement;
        var vendor = String(root, "vendor_data");
        if (SessionId(String(root, "session_id")) != id || !Guid.TryParseExact(String(root, "workflow_id"), "D", out var workflow)
            || workflow.ToString("D") != configuration.ConfigurationId || string.IsNullOrWhiteSpace(vendor) || vendor.Length > 64
            || String(root, "environment") != (configuration.Sandbox ? "sandbox" : "live")) throw Unavailable();
        var status = String(root, "status") switch
        {
            "Not Started" or "In Progress" or "Awaiting User" => KycStatus.InProgress,
            "Approved" => KycStatus.Verified,
            "Declined" => KycStatus.Rejected,
            "Resubmitted" => KycStatus.NeedsResubmission,
            "Expired" or "Kyc Expired" or "Abandoned" => KycStatus.Expired,
            _ => KycStatus.PendingReview
        };
        return new KycSessionSnapshot(id, vendor, configuration.ConfigurationId, configuration.Sandbox, status,
            status == KycStatus.Verified && RequirementsMet(root, id), VerificationUrl(String(root, "session_url")));
    }

    public static string? VerificationUrl(string? value)
    {
        if (value is null) return null;
        if (value.Length > 4096 || !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https"
            || uri.IdnHost != "verify.didit.me" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0
            || !uri.AbsolutePath.StartsWith("/session/", StringComparison.Ordinal) || uri.AbsolutePath.Length <= 9) throw Unavailable();
        return uri.AbsoluteUri;
    }

    private static bool RequirementsMet(JsonElement root, string sessionId) =>
        AllApproved(root, "id_verifications", item => String(item, "issuing_state") == "UKR" && DocumentAllowed(String(item, "document_type")))
        && AllApproved(root, "liveness_checks", item => string.Equals(String(item, "method"), "PASSIVE", StringComparison.OrdinalIgnoreCase))
        && AllApproved(root, "face_matches", item => String(item, "source_image_session_id") is not { } source || source == sessionId);

    private static bool DocumentAllowed(string? value) => value?.Trim().ToUpperInvariant().Replace(' ', '_').Replace('-', '_')
        is "P" or "PASSPORT" or "EPASSPORT" or "E_PASSPORT" or "ID" or "ID_CARD" or "IDENTITY_CARD" or "NATIONAL_ID" or "NATIONAL_IDENTITY_CARD";
    private static bool AllApproved(JsonElement root, string name, Func<JsonElement, bool> requirements) =>
        root.TryGetProperty(name, out var items) && items.ValueKind == JsonValueKind.Array && items.GetArrayLength() > 0
        && items.EnumerateArray().All(item => item.ValueKind == JsonValueKind.Object && String(item, "status") == "Approved" && requirements(item));

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, byte[]? body, CancellationToken ct)
    {
        _ = Configuration;
        try
        {
            using var request = new HttpRequestMessage(method, "https://verification.didit.me" + path);
            request.Headers.Add("X-Api-Key", options.Value.ApiKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (body is not null) { request.Content = new ByteArrayContent(body); request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json"); }
            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) throw Unavailable();
            var json = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(ct));
            if (json.RootElement.ValueKind != JsonValueKind.Object) { json.Dispose(); throw Unavailable(); }
            return json;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or IOException
            || exception is OperationCanceledException && !ct.IsCancellationRequested) { throw Unavailable(); }
    }
    private static string SessionId(string? value) => Guid.TryParseExact(value, "D", out var id) && id != Guid.Empty ? id.ToString("D") : throw Unavailable();
    private static string? String(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static ApplicationFailureException Unavailable() => new(FailureKind.Unavailable, "kyc.provider_unavailable", "Identity verification is temporarily unavailable. Try again later.");
}
