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
