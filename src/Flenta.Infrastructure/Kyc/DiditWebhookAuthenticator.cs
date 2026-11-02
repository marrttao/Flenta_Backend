using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Flenta.Application.Abstractions;
using Flenta.Application.Abstractions.Kyc;
using Flenta.Application.Features.Auth;
using Microsoft.Extensions.Options;

namespace Flenta.Infrastructure.Kyc;

public sealed class DiditWebhookAuthenticator(IOptions<DiditOptions> options, IClock clock) : IKycWebhookAuthenticator
{
    public bool Verify(ReadOnlySpan<byte> body, string signature, string canonicalSignature, string timestamp)
    {
        if (!options.Value.Configured || signature.Length != 64 && canonicalSignature.Length != 64) return false;
        var key = Encoding.UTF8.GetBytes(options.Value.WebhookSecret);
        try
        {
            using var json = JsonDocument.Parse(body.ToArray(), new JsonDocumentOptions { MaxDepth = 32 });
            if (json.RootElement.ValueKind != JsonValueKind.Object) return false;
            var canonical = Canonical(json.RootElement);
            var authentic = Matches(key, body, signature) || Matches(key, Encoding.UTF8.GetBytes(canonical), canonicalSignature);
            if (!authentic) return false;
            var root = json.RootElement;
            var stamp = root.TryGetProperty("timestamp", out var value) ? Unix(value)
                : root.TryGetProperty("created_at", out var created) ? CreatedAt(created) : null;
            return stamp is not null && stamp >= 0 && Math.Abs((decimal)clock.UtcNow.ToUnixTimeSeconds() - stamp.Value) <= 300
                && long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var header) && header == stamp;
        }
        catch (Exception exception) when (exception is JsonException or FormatException or InvalidOperationException or OverflowException) { return false; }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    public KycNotification? ReadNotification(byte[] body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            var kind = String(root, "webhook_type");
            if (kind is not null && kind is not ("status.updated" or "data.updated")) return null;
            if (!Guid.TryParseExact(String(root, "session_id"), "D", out var session) || session == Guid.Empty) return null;
            var workflow = String(root, "workflow_id");
            if (workflow is not null)
            {
                if (!Guid.TryParseExact(workflow, "D", out var id) || id == Guid.Empty) throw new JsonException();
                workflow = id.ToString("D");
            }
            var environment = String(root, "environment");
            if (environment is not null && environment is not ("live" or "sandbox")) throw new JsonException();
            var eventId = String(root, "event_id");
            if (eventId?.Length > 128) throw new JsonException();
            var digest = Convert.ToHexString(SHA256.HashData(string.IsNullOrWhiteSpace(eventId) ? body : Encoding.UTF8.GetBytes("Didit|" + eventId)));
            return new KycNotification("Didit", session.ToString("D"), String(root, "vendor_data"), workflow,
                environment is null ? null : environment == "sandbox", digest);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        { throw new ApplicationFailureException(FailureKind.InvalidRequest, "kyc.webhook_invalid_json", "Invalid webhook payload."); }
    }

    private static bool Matches(byte[] key, ReadOnlySpan<byte> data, string signature)
    {
        if (signature.Length != 64) return false;
        try { return CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(key, data), Convert.FromHexString(signature)); }
        catch (FormatException) { return false; }
    }
    private static long? Unix(JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? number
        : value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var text) ? text : null;
    private static long? CreatedAt(JsonElement value) => Unix(value) ?? (value.ValueKind == JsonValueKind.String
        && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time) ? time.ToUnixTimeSeconds() : null);
    private static string? String(JsonElement root, string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    // Didit V2 signs recursively sorted, compact JSON with literal Unicode and shortened floats.
    private static string Canonical(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => Object(value),
        JsonValueKind.Array => "[" + string.Join(',', value.EnumerateArray().Select(Canonical)) + "]",
        JsonValueKind.String => Quote(value.GetString()!),
        JsonValueKind.Number => Number(value.GetDouble()),
        JsonValueKind.True => "true", JsonValueKind.False => "false", JsonValueKind.Null => "null",
        _ => throw new JsonException()
    };
    private static string Object(JsonElement value)
    {
        var fields = value.EnumerateObject().ToArray();
        if (fields.Select(f => f.Name).Distinct(StringComparer.Ordinal).Count() != fields.Length) throw new JsonException();
        Array.Sort(fields, (a, b) => Encoding.UTF8.GetBytes(a.Name).AsSpan().SequenceCompareTo(Encoding.UTF8.GetBytes(b.Name)));
        return "{" + string.Join(',', fields.Select(f => Quote(f.Name) + ":" + Canonical(f.Value))) + "}";
    }
    private static string Quote(string text)
    {
        var result = new StringBuilder("\"");
        foreach (var rune in text.EnumerateRunes())
            result.Append(rune.Value switch
            {
                34 => "\\\"", 92 => "\\\\", 8 => "\\b", 9 => "\\t", 10 => "\\n", 12 => "\\f", 13 => "\\r",
                < 32 => "\\u" + rune.Value.ToString("x4", CultureInfo.InvariantCulture),
                _ => rune.ToString()
            });
        return result.Append('"').ToString();
    }
    private static string Number(double number)
    {
        if (!double.IsFinite(number)) throw new JsonException();
        if (number == 0) return "0";
        var text = number.ToString("R", CultureInfo.InvariantCulture);
        var e = text.IndexOf('E');
        if (e < 0) return text;
        var exponent = int.Parse(text[(e + 1)..], CultureInfo.InvariantCulture);
        var mantissa = text[..e];
        if (exponent < -6 || exponent >= 21) return mantissa + "e" + (exponent >= 0 ? "+" : "") + exponent.ToString(CultureInfo.InvariantCulture);
        var negative = mantissa.StartsWith('-');
        var digits = mantissa.TrimStart('-').Replace(".", "");
        var position = exponent + 1;
        var expanded = position <= 0 ? "0." + new string('0', -position) + digits
            : position >= digits.Length ? digits + new string('0', position - digits.Length) : digits.Insert(position, ".");
        return (negative ? "-" : "") + expanded;
    }
}
