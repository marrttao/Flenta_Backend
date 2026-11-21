using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Flenta.Application.Features.Auth;
using Flenta.Infrastructure.Authentication;
using Flenta.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Flenta.Infrastructure.Persistence.Banking;
using Microsoft.EntityFrameworkCore;

namespace Flenta.Tests.Admin;

public sealed class AdminDirectoryTests
{
    [Fact]
    public void Administration_does_not_require_a_database_model_change()
    {
        using var context = new BankingDbContext(new DbContextOptionsBuilder<BankingDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=unused").Options);
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Directory_snapshots_detect_incomplete_or_changing_pagination(bool corruptSecondPage)
    {
        var calls = 0;
        using var handler = new CallbackHandler(request =>
        {
            calls++;
            var second = request.RequestUri!.Query.Contains("page=2", StringComparison.Ordinal);
            var start = second && !corruptSecondPage ? 1000 : 0;
            var count = second ? 1 : 1000;
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new
            {
                users = Enumerable.Range(start, count).Select(i => new { id = Guid.Parse($"44000000-0000-4000-8000-{i + 1:000000000000}"),
                    email = $"client{i}@example.com", created_at = "2026-11-01T00:00:00Z" }).ToArray()
            }) };
            response.Headers.Add("x-total-count", "1001");
            return response;
        });
        using var http = new HttpClient(handler);
        if (corruptSecondPage) await Assert.ThrowsAsync<ApplicationFailureException>(() => Directory(http).AllAsync(default));
        else Assert.Equal(1001, (await Directory(http).AllAsync(default)).Count);
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Server_credentials_pagination_and_email_filter_are_sent_only_to_the_configured_provider(bool legacy)
    {
        var key = legacy ? LegacyKey("service_role") : "sb_secret_test_fixture";
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        var directory = Directory(http, key);
        var page = await directory.ListAsync(2, 8, "alex+bank@example.com", default);
        Assert.Equal("project.supabase.co", handler.Uri?.Host);
        Assert.Equal("/auth/v1/admin/users", handler.Uri?.AbsolutePath);
        Assert.Contains("page=2&per_page=8&filter=alex%2Bbank%40example.com", handler.Uri?.Query);
        Assert.Equal(key, handler.ApiKey);
        Assert.Equal(legacy ? key : null, handler.Bearer);
        Assert.Equal(1, page.Total);
        Assert.Equal("Alex", Assert.Single(page.Users).DisplayName);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"users\":null}")]
    [InlineData("{\"users\":[null]}")]
    [InlineData("{\"users\":[{\"id\":42}]}")]
    public async Task Corrupt_directory_responses_return_a_controlled_503(string body)
    {
        using var handler = new Handler { Body = body };
        using var http = new HttpClient(handler);
        var failure = await Assert.ThrowsAsync<ApplicationFailureException>(() => Directory(http).ListAsync(1, 8, "", default));
        Assert.Equal(FailureKind.Unavailable, failure.Kind);
    }

    [Fact]
    public async Task Missing_total_is_not_silently_rendered_as_an_empty_directory()
    {
        using var handler = new Handler { IncludeTotal = false };
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<ApplicationFailureException>(() => Directory(http).ListAsync(1, 8, "", default));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, FailureKind.Unavailable)]
    [InlineData(HttpStatusCode.ServiceUnavailable, FailureKind.Unavailable)]
    [InlineData(HttpStatusCode.TooManyRequests, FailureKind.RateLimited)]
    [InlineData(HttpStatusCode.Redirect, FailureKind.Unavailable)]
    public async Task Provider_errors_do_not_leak_keys_or_end_the_callers_session(HttpStatusCode status, FailureKind kind)
    {
        using var handler = new Handler { Status = status };
        using var http = new HttpClient(handler);
        var failure = await Assert.ThrowsAsync<ApplicationFailureException>(() => Directory(http).ListAsync(1, 8, "", default));
        Assert.Equal(kind, failure.Kind);
        Assert.DoesNotContain("sb_secret", failure.Message);
    }

    [Fact]
    public async Task Empty_configuration_does_not_send_a_privileged_request()
    {
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        var failure = await Assert.ThrowsAsync<ApplicationFailureException>(() => Directory(http, "").ListAsync(1, 8, "", default));
        Assert.Equal("admin.directory_not_configured", failure.Code);
        Assert.Null(handler.Uri);
    }

    [Fact]
    public async Task Caller_cancellation_is_preserved()
    {
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Directory(http).ListAsync(1, 8, "", cancellation.Token));
    }

    [Fact]
    public void Public_and_user_keys_are_not_accepted_as_administrative_configuration()
    {
        Assert.False(AdminOptions.IsServerKey("sb_publishable_fixture"));
        Assert.False(AdminOptions.IsServerKey(LegacyKey("anon")));
        Assert.False(AdminOptions.IsServerKey(LegacyKey("authenticated")));
        Assert.True(AdminOptions.IsServerKey(LegacyKey("service_role")));
        Assert.True(AdminOptions.IsServerKey("sb_secret_fixture"));
    }

    private static SupabaseAdminDirectory Directory(HttpClient http, string key = "sb_secret_test_fixture") =>
        new(http, Options.Create(new SupabaseOptions { Enabled = true, Url = "https://project.supabase.co" }),
            Options.Create(new AdminOptions { SupabaseSecretKey = key }));
    private static string LegacyKey(string role) =>
        "header." + Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { role }))).TrimEnd('=') + ".signature";

    private sealed class Handler : HttpMessageHandler
    {
        public string? Body { get; init; }
        public bool IncludeTotal { get; init; } = true;
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        public Uri? Uri { get; private set; }
        public string? ApiKey { get; private set; }
        public string? Bearer { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Uri = request.RequestUri;
            ApiKey = Assert.Single(request.Headers.GetValues("apikey"));
            Bearer = request.Headers.Authorization?.Parameter;
            var response = new HttpResponseMessage(Status)
            {
                Content = Body is not null ? new StringContent(Body) : JsonContent.Create(new
                {
                    users = new[] { new { id = Guid.NewGuid(), email = "alex@example.com", created_at = "2026-11-01T00:00:00Z",
                        email_confirmed_at = "2026-11-01T00:00:00Z", user_metadata = new { display_name = "Alex" } } }
                })
            };
            if (IncludeTotal) response.Headers.Add("x-total-count", "1");
            return Task.FromResult(response);
        }
    }

    private sealed class CallbackHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(response(request));
    }
}
