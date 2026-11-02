using System.Text;
using System.Text.Json;
using Flenta.Infrastructure.Configuration;

namespace Flenta.Tests.Api;

public sealed class SupabaseConfigurationTests
{
    [Fact]
    public void Administrative_keys_are_not_accepted_as_application_keys()
    {
        Assert.False(SupabaseOptions.IsPublicKey("sb_secret_test_fixture"));
        Assert.False(SupabaseOptions.IsPublicKey("sbp_test_fixture"));
        Assert.False(SupabaseOptions.IsPublicKey(LegacyKey("service_role")));
        Assert.True(SupabaseOptions.IsPublicKey("sb_publishable_test_fixture"));
        Assert.True(SupabaseOptions.IsPublicKey(LegacyKey("anon")));
    }

    [Theory]
    [InlineData("http://127.0.0.1:9999", true)]
    [InlineData("http://localhost:9999", true)]
    [InlineData("https://project.supabase.co", true)]
    [InlineData("http://project.supabase.co", false)]
    [InlineData("https://user:password@project.supabase.co", false)]
    public void Remote_connections_require_TLS_and_do_not_embed_credentials(string url, bool expected)
    {
        Assert.Equal(expected, SupabaseOptions.IsSafeUrl(url));
    }

    private static string LegacyKey(string role)
    {
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { role }))).TrimEnd('=');
        return "header." + payload + ".signature";
    }
}
