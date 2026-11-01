using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flenta.Application.Abstractions;
using Flenta.Application.Abstractions.Banking;
using Flenta.Application.Features.Auth;

namespace Flenta.Infrastructure.Exchange;

public sealed class NbuExchangeRateProvider(HttpClient client, IClock clock) : IExchangeRateProvider
{
    public async Task<CurrencyRates> GetAsync(CancellationToken cancellationToken)
    {
        try
        {
            var rows = await client.GetFromJsonAsync<NbuRate[]>("https://bank.gov.ua/NBUStatService/v1/statdirectory/exchange?json", cancellationToken)
                ?? throw new JsonException();
            var usd = rows.Single(row => row.Code == "USD");
            var eur = rows.Single(row => row.Code == "EUR");
            var date = DateOnly.ParseExact(usd.Date, "dd.MM.yyyy", CultureInfo.InvariantCulture);
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Europe/Kyiv")).DateTime);
            if (usd.Rate <= 0 || eur.Rate <= 0 || usd.Date != eur.Date || date > today || today.DayNumber - date.DayNumber > 7)
                throw new JsonException();
            return new CurrencyRates(date, "NBU", new Dictionary<string, decimal> { ["UAH"] = 1m, ["USD"] = usd.Rate, ["EUR"] = eur.Rate });
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or FormatException or InvalidOperationException or ArgumentException
            || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new ApplicationFailureException(FailureKind.Unavailable, "exchange.rates_unavailable", "NBU exchange rates are unavailable. Try again later.");
        }
    }
    private sealed record NbuRate([property: JsonPropertyName("cc")] string Code,
        [property: JsonPropertyName("rate")] decimal Rate, [property: JsonPropertyName("exchangedate")] string Date);
}
