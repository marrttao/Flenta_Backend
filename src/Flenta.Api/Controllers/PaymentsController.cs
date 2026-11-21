using System.ComponentModel.DataAnnotations;
using Flenta.Application.Features.Payments;
using Flenta.Application.Features.Transactions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
namespace Flenta.Api.Controllers;

[ApiController, Authorize, Route("api/payments"), EnableRateLimiting("banking"), RequestSizeLimit(16384)]
public sealed class PaymentsController(PaymentService service) : ControllerBase
{
    [HttpPost]
    public Task<TransactionResponse> Pay(SandboxPaymentRequest request, [FromHeader(Name = "Idempotency-Key")] Guid key, CancellationToken ct) =>
        service.PayAsync(request.AccountId, request.Amount, request.Kind, request.Reference, key, ct);
    [HttpPost("card-transfers")]
    public Task<TransactionResponse> Transfer(CardTransferRequest request, [FromHeader(Name = "Idempotency-Key")] Guid key, CancellationToken ct) =>
        service.CardTransferAsync(request.SourceAccountId, request.Number, request.Amount, request.Memo, key, ct);
}
public sealed record SandboxPaymentRequest(Guid AccountId, decimal Amount, [Required, StringLength(32)] string Kind, [Required, StringLength(50)] string Reference);
public sealed record CardTransferRequest(Guid SourceAccountId, [Required, StringLength(24)] string Number, decimal Amount, [StringLength(280)] string? Memo = null);
[ApiController, Authorize, Route("api/statements"), EnableRateLimiting("banking")]
public sealed class StatementsController(StatementService service) : ControllerBase
{
    [HttpGet] public Task<IReadOnlyList<TransactionResponse>> Get(DateOnly from, DateOnly through, CancellationToken ct) => service.GetAsync(from, through, ct);
}
