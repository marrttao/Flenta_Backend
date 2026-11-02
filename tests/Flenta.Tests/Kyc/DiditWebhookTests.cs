using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Flenta.Infrastructure.Kyc;
using Flenta.Tests.Banking;
using Microsoft.Extensions.Options;

namespace Flenta.Tests.Kyc;

public sealed class DiditWebhookTests
{
    private const long Now = 1793538000;
    private static DiditWebhookAuthenticator Verifier() => new(Options.Create(DiditClientTests.Settings()), new TestClock { UtcNow = DateTimeOffset.FromUnixTimeSeconds(Now) });
    // Fixed external Node.js signatures: recursively sorted keys, literal Unicode, shortened numbers.
    private const string Golden = """{"timestamp":1793538000,"session_id":"ab000000-0000-0000-0000-000000000001","vendor_data":"flenta-10000000000000000000000000000001","workflow_id":"bc000000-0000-0000-0000-000000000001","environment":"sandbox","webhook_type":"status.updated","event_id":"golden","metadata":{"Ім’я":"Оля 😀","z":1,"a":[0.000001,1e-7,1e+21,0,0.71]}}""";
    private const string V2 = "927e4e330728f1dc15a0825cdd9bafd9796c77ef20d54d2d8c1222469788cd86";
    private const string Raw = "8f6eb32857990a1ab2937244c339877afe42b1c9e91eadd82932b3a3b8ef8ea9";

    [Fact]
    public void Raw_and_canonical_V2_signatures_match_external_golden_vectors()
    {
        var verifier = Verifier(); var body = Encoding.UTF8.GetBytes(Golden); var stamp = Now.ToString(CultureInfo.InvariantCulture);
        Assert.True(verifier.Verify(body, Raw, "", stamp)); Assert.True(verifier.Verify(body, "", V2, stamp));
        var reformatted = Encoding.UTF8.GetBytes(Golden.Replace(",", ", "));
        Assert.False(verifier.Verify(reformatted, Raw, "", stamp)); Assert.True(verifier.Verify(reformatted, "", V2, stamp));
        Assert.False(verifier.Verify(Encoding.UTF8.GetBytes(Golden.Replace("sandbox", "live")), Raw, V2, stamp));
    }

    [Fact]
    public void Unsigned_header_cannot_extend_signed_payload_freshness()
    {
        var verifier = Verifier(); var body = Encoding.UTF8.GetBytes(Golden);
        Assert.False(verifier.Verify(body, Raw, V2, (Now + 1).ToString(CultureInfo.InvariantCulture)));
        foreach (var time in new[] { Now - 301, Now + 301 })
        {
            var stale = Encoding.UTF8.GetBytes(Golden.Replace(Now.ToString(CultureInfo.InvariantCulture), time.ToString(CultureInfo.InvariantCulture)));
            var signature = Sign(stale);
            Assert.False(verifier.Verify(stale, signature, "", time.ToString(CultureInfo.InvariantCulture)));
            Assert.False(verifier.Verify(stale, signature, "", Now.ToString(CultureInfo.InvariantCulture)));
        }
    }

    [Fact]
    public void Missing_timestamp_simple_signatures_and_duplicate_keys_are_rejected()
    {
        var verifier = Verifier(); var stamp = Now.ToString(CultureInfo.InvariantCulture);
        foreach (var json in new[] { "{}", "[]", "not-json", "{\"timestamp\":1793538000,\"timestamp\":1793538000}" })
        {
            var bytes = Encoding.UTF8.GetBytes(json); Assert.False(verifier.Verify(bytes, Sign(bytes), "", stamp));
        }
        Assert.False(verifier.Verify(Encoding.UTF8.GetBytes(Golden), "", "", stamp));
        Assert.False(verifier.Verify(Encoding.UTF8.GetBytes(Golden), new string('z', 64), "", stamp));
    }

    [Fact]
    public void Retry_delivery_timestamps_do_not_change_event_deduplication_key()
    {
        var verifier = Verifier();
        var original = verifier.ReadNotification(Encoding.UTF8.GetBytes(Golden))!;
        var retry = verifier.ReadNotification(Encoding.UTF8.GetBytes(Golden.Replace("1793538000", "1793538001")))!;
        Assert.Equal(original.Digest, retry.Digest); Assert.Equal(KycTestProvider.SessionId, original.SessionId);
        Assert.Equal("Didit", original.Provider); Assert.True(original.Sandbox);
    }
    private static string Sign(byte[] bytes) => Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes("hook-fixture"), bytes));
}
