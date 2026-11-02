using System.ComponentModel.DataAnnotations;
using Flenta.Application.Features.Transactions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Flenta.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/transactions")]
[EnableRateLimiting("banking")]
[RequestSizeLimit(16 * 1024)]
public sealed class TransactionsController(TransactionService transactions) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<TransactionResponse>> List(CancellationToken cancellationToken, [FromQuery] int offset = 0, [FromQuery] int limit = 50) =>
        transactions.ListAsync(offset, limit, cancellationToken);

    [HttpGet("{id:guid}")]
    public Task<TransactionResponse> Get(Guid id, CancellationToken cancellationToken) => transactions.GetAsync(id, cancellationToken);

    [HttpPost("internal")]
    public Task<TransactionResponse> Internal(InternalTransferRequest request, [FromHeader(Name = "Idempotency-Key")] Guid idempotencyKey, CancellationToken cancellationToken) =>
        transactions.TransferAsync(request.SourceAccountId, request.DestinationAccountId, request.Amount, idempotencyKey, cancellationToken);

    [HttpPost("quote")]
    public Task<TransferQuote> Quote(TransferQuoteRequest request, CancellationToken cancellationToken) =>
        transactions.QuoteAsync(request.SourceAccountId, request.Amount, request.ByIban, cancellationToken);

    [HttpPost("iban")]
    public Task<TransactionResponse> ByIban(IbanTransferRequest request, [FromHeader(Name = "Idempotency-Key")] Guid idempotencyKey, CancellationToken cancellationToken) =>
        transactions.TransferByIbanAsync(request.SourceAccountId, request.Iban, request.Amount, idempotencyKey, cancellationToken);
}

public sealed record InternalTransferRequest(Guid SourceAccountId, Guid DestinationAccountId, decimal Amount);
public sealed record TransferQuoteRequest(Guid SourceAccountId, decimal Amount, bool ByIban);
public sealed record IbanTransferRequest(Guid SourceAccountId, [Required, StringLength(64)] string Iban, decimal Amount);
