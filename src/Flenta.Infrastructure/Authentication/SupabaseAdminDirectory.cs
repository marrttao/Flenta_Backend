using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Flenta.Application.Features.Auth;
using Flenta.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Flenta.Infrastructure.Authentication;

internal sealed record DirectoryUser(Guid Id, string DisplayName, string? Email, bool EmailConfirmed, DateTimeOffset CreatedAt);
internal sealed record DirectoryPage(IReadOnlyList<DirectoryUser> Users, int Total);

internal sealed class SupabaseAdminDirectory(HttpClient client, IOptions<SupabaseOptions> supabase, IOptions<AdminOptions> admin)
{
    public async Task<DirectoryPage> ListAsync(int page, int pageSize, string email, CancellationToken ct)
    {
        var path = $"users?page={page}&per_page={pageSize}";
        if (email.Length > 0) path += "&filter=" + Uri.EscapeDataString(email.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_"));
        var (json, total) = await GetAsync(path, ct);
        using (json)
        {
            if (json is null || total is null || json.RootElement.ValueKind != JsonValueKind.Object
                || !json.RootElement.TryGetProperty("users", out var users) || users.ValueKind != JsonValueKind.Array
                || users.GetArrayLength() > pageSize || users.GetArrayLength() > total.Value) throw Unavailable();
            return new DirectoryPage(users.EnumerateArray().Select(ReadUser).ToArray(), total.Value);
        }
    }

    public async Task<IReadOnlyList<DirectoryUser>> AllAsync(CancellationToken ct)
    {
        var first = await ListAsync(1, 1000, "", ct);
        // Bound administrative snapshots; never return a truncated count as a complete result.
        if (first.Total > 100000) throw Unavailable();
        var users = new List<DirectoryUser>(first.Users);
        for (var page = 2; (page - 1L) * 1000 < first.Total; page++)
        {
            var next = await ListAsync(page, 1000, "", ct);
            if (next.Total != first.Total) throw Unavailable();
            users.AddRange(next.Users);
        }
        if (users.Select(u => u.Id).Distinct().Count() != first.Total) throw Unavailable();
        return users;
    }

    private async Task<(JsonDocument Json, int? Total)> GetAsync(string path, CancellationToken ct)
    {
        if (!supabase.Value.Enabled || string.IsNullOrWhiteSpace(admin.Value.SupabaseSecretKey))
            throw new ApplicationFailureException(FailureKind.Unavailable, "admin.directory_not_configured", "The client directory is unavailable. Contact the administrator.");
        using var request = new HttpRequestMessage(HttpMethod.Get, supabase.Value.Url.TrimEnd('/') + "/auth/v1/admin/" + path);
        request.Headers.Add("apikey", admin.Value.SupabaseSecretKey);
        if (!admin.Value.SupabaseSecretKey.StartsWith("sb_secret_", StringComparison.Ordinal))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", admin.Value.SupabaseSecretKey);
        try
        {
            using var response = await client.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                throw new ApplicationFailureException(FailureKind.RateLimited, "admin.rate_limited", "The client directory is busy. Try again later.");
            if (!response.IsSuccessStatusCode) throw Unavailable();
            int? total = null;
            if (response.Headers.TryGetValues("x-total-count", out var values)
                && int.TryParse(values.SingleOrDefault(), NumberStyles.None, CultureInfo.InvariantCulture, out var count) && count >= 0) total = count;
            var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            return (json, total);
        }
        catch (Exception failure) when (failure is HttpRequestException or IOException or JsonException or InvalidOperationException
            || failure is OperationCanceledException && !ct.IsCancellationRequested)
        {
            throw Unavailable();
        }
    }

    private static DirectoryUser ReadUser(JsonElement user)
    {
        if (user.ValueKind != JsonValueKind.Object || !user.TryGetProperty("id", out var idValue)
            || idValue.ValueKind != JsonValueKind.String || !Guid.TryParse(idValue.GetString(), out var id) || id == Guid.Empty
            || !user.TryGetProperty("created_at", out var created) || created.ValueKind != JsonValueKind.String
            || !created.TryGetDateTimeOffset(out var createdAt)) throw Unavailable();
        var email = user.TryGetProperty("email", out var emailValue) && emailValue.ValueKind == JsonValueKind.String ? emailValue.GetString() : null;
        if (email?.Length > 254 || email?.Any(char.IsControl) == true) throw Unavailable();
        var name = email ?? id.ToString("D");
        if (user.TryGetProperty("user_metadata", out var data) && data.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in new[] { "display_name", "full_name", "name" })
                if (data.TryGetProperty(property, out var text) && text.ValueKind == JsonValueKind.String
                    && text.GetString()?.Trim() is { Length: > 0 and <= 100 } candidate && !candidate.Any(char.IsControl))
                { name = candidate; break; }
        }
        var confirmed = user.TryGetProperty("email_confirmed_at", out var confirmedAt) && confirmedAt.ValueKind == JsonValueKind.String
            && confirmedAt.TryGetDateTimeOffset(out _);
        return new DirectoryUser(id, name, email, confirmed, createdAt);
    }

    private static ApplicationFailureException Unavailable() =>
        new(FailureKind.Unavailable, "admin.directory_unavailable", "Client information is temporarily unavailable. Try again later.");
}
