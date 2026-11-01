using Flenta.Application.Abstractions.Banking;
using Flenta.Application.Features.Finance;
using Flenta.Application.Features.Transactions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Flenta.Api.Controllers;

[ApiController, Authorize, Route("api/finance"), EnableRateLimiting("banking"), RequestSizeLimit(16 * 1024)]
public sealed class FinanceController(ExchangeService exchange, PurchaseService purchases, AnalyticsService analytics) : ControllerBase
{
    [HttpGet("rates")]
    public Task<CurrencyRates> Rates(CancellationToken ct) => exchange.RatesAsync(ct);

    [HttpPost("exchange-quotes")]
    public Task<ExchangeQuoteResponse> Quote(CurrencyExchangeRequest request, CancellationToken ct) =>
        exchange.QuoteAsync(request.SourceAccountId, request.DestinationAccountId, request.Amount, ct);

    [HttpPost("exchanges")]
    public Task<TransactionResponse> Exchange(ConfirmExchangeRequest request, [FromHeader(Name = "Idempotency-Key")] Guid key, CancellationToken ct) =>
        exchange.ExchangeAsync(request.QuoteId, key, ct);

    [HttpPost("purchases")]
    public Task<TransactionResponse> Purchase(PurchaseRequest request, [FromHeader(Name = "Idempotency-Key")] Guid key, CancellationToken ct) =>
        purchases.PayAsync(request.CardId, request.Amount, key, ct);

    [HttpGet("cashback")]
    public Task<CashbackResponse> Cashback(CancellationToken ct, [FromQuery] int offset = 0, [FromQuery] int limit = 50) =>
        purchases.CashbackAsync(offset, limit, ct);

    [HttpGet("analytics")]
    public Task<AnalyticsResponse> Analytics([FromQuery] DateOnly from, [FromQuery] DateOnly through, CancellationToken ct) =>
        analytics.GetAsync(from, through, ct);
}

public sealed record CurrencyExchangeRequest(Guid SourceAccountId, Guid DestinationAccountId, decimal Amount);
public sealed record ConfirmExchangeRequest(Guid QuoteId);
public sealed record PurchaseRequest(Guid CardId, decimal Amount);
