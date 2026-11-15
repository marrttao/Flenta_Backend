using System.Net;
using System.Net.Http.Json;
using Flenta.Application.Features.Auth;
using Flenta.Infrastructure.Exchange;

namespace Flenta.Tests.Banking;

public sealed class NbuRateTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Nbu_official_quotes_produce_a_dated_three_currency_snapshot()
    {
        using var handler = new Handler(HttpStatusCode.OK, "01.11.2026", "01.11.2026", 40, 45);
        using var client = new HttpClient(handler);
        var result = await new NbuExchangeRateProvider(client, new TestClock { UtcNow = Now }).GetAsync(default);
        Assert.Equal("bank.gov.ua", handler.Request!.RequestUri!.Host);
        Assert.Equal("NBU", result.Source); Assert.Equal(new DateOnly(2026, 11, 1), result.Date);
        Assert.Equal(1, result.UahPerUnit["UAH"]); Assert.Equal(40, result.UahPerUnit["USD"]); Assert.Equal(45, result.UahPerUnit["EUR"]);
    }

    [Theory]
    [InlineData("01.11.2026", "01.11.2026", -1)]
    [InlineData("02.11.2026", "02.11.2026", 40)]
    [InlineData("01.10.2026", "01.10.2026", 40)]
    [InlineData("01.11.2026", "31.10.2026", 40)]
    [InlineData("invalid", "invalid", 40)]
    public async Task Invalid_future_or_stale_rates_are_not_used(string usdDate, string eurDate, decimal usd)
    {
        using var handler = new Handler(HttpStatusCode.OK, usdDate, eurDate, usd, 45);
        using var client = new HttpClient(handler);
        var failure = await Assert.ThrowsAsync<ApplicationFailureException>(() => new NbuExchangeRateProvider(client, new TestClock { UtcNow = Now }).GetAsync(default));
        Assert.Equal(FailureKind.Unavailable, failure.Kind);
    }

    [Fact]
    public async Task Upstream_http_failure_is_reported_as_temporarily_unavailable()
    {
        using var handler = new Handler(HttpStatusCode.Forbidden, "", "", 0, 0);
        using var client = new HttpClient(handler);
        var failure = await Assert.ThrowsAsync<ApplicationFailureException>(() => new NbuExchangeRateProvider(client, new TestClock { UtcNow = Now }).GetAsync(default));
        Assert.Equal("exchange.rates_unavailable", failure.Code);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[null]")]
    [InlineData("[null,{\"cc\":\"USD\",\"rate\":40,\"exchangedate\":\"01.11.2026\"},{\"cc\":\"EUR\",\"rate\":45,\"exchangedate\":\"01.11.2026\"}]")]
    [InlineData("[{\"cc\":\"USD\",\"rate\":40,\"exchangedate\":null},{\"cc\":\"EUR\",\"rate\":45,\"exchangedate\":null}]")]
    [InlineData("[]")]
    public async Task Malformed_rate_snapshots_are_reported_as_unavailable(string content)
    {
        using var handler = new RawHandler(content);
        using var client = new HttpClient(handler);
        var failure = await Assert.ThrowsAsync<ApplicationFailureException>(() => new NbuExchangeRateProvider(client, new TestClock { UtcNow = Now }).GetAsync(default));
        Assert.Equal(FailureKind.Unavailable, failure.Kind);
        Assert.Equal("exchange.rates_unavailable", failure.Code);
    }

    [Fact]
    public async Task Interrupted_rate_response_is_reported_as_unavailable()
    {
        using var handler = new RawHandler(null);
        using var client = new HttpClient(handler);
        var failure = await Assert.ThrowsAsync<ApplicationFailureException>(() => new NbuExchangeRateProvider(client, new TestClock { UtcNow = Now }).GetAsync(default));
        Assert.Equal("exchange.rates_unavailable", failure.Code);
    }

    private sealed class RawHandler(string? content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            content is null ? throw new IOException("private-transport-error")
                : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content) });
    }

    private sealed class Handler(HttpStatusCode status, string usdDate, string eurDate, decimal usd, decimal eur) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(status) { Content = JsonContent.Create(new[]
            {
                new { cc = "USD", rate = usd, exchangedate = usdDate }, new { cc = "EUR", rate = eur, exchangedate = eurDate }
            }) });
        }
    }
}
