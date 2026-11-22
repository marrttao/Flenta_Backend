using System.Text;
using System.Text.Json;

namespace Flenta.Infrastructure.Configuration;

public sealed class AdminOptions
{
    public string SupabaseSecretKey { get; set; } = "";

    public static bool IsServerKey(string key)
    {
        if (key.StartsWith("sb_secret_", StringComparison.Ordinal)) return key.Length > "sb_secret_".Length;
        var parts = key.Split('.');
        if (parts.Length != 3) return false;
        try
        {
            var body = parts[1].Replace('-', '+').Replace('_', '/');
            using var json = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(body.PadRight((body.Length + 3) / 4 * 4, '='))));
            return json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("role", out var role)
                && role.ValueKind == JsonValueKind.String && role.GetString() == "service_role";
        }
        catch (Exception exception) when (exception is FormatException or JsonException) { return false; }
    }
}
