using System.Text;
using System.Text.Json;

namespace Flenta.Infrastructure.Configuration;

public sealed class SupabaseOptions
{
    public bool Enabled { get; set; }
    public string Url { get; set; } = string.Empty;
    public string PublishableKey { get; set; } = string.Empty;
    public string FrontendUrl { get; set; } = "http://127.0.0.1:5173";

    public static bool IsSafeUrl(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)
        && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);

    public static bool IsPublicKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (value.StartsWith("sb_publishable_", StringComparison.Ordinal)) return value.Length > "sb_publishable_".Length;
        var parts = value.Split('.');
        if (parts.Length != 3) return false;
        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight((payload.Length + 3) / 4 * 4, '=');
            using var data = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
            return data.RootElement.TryGetProperty("role", out var role) && role.GetString() == "anon";
        }
        catch (Exception exception) when (exception is FormatException or JsonException or InvalidOperationException)
        {
            return false;
        }
    }
}
