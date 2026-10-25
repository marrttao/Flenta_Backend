using Flenta.Application.Features.Accounts;
using Flenta.Application.Features.Transactions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Flenta.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/accounts")]
[EnableRateLimiting("banking")]
[RequestSizeLimit(16 * 1024)]
public sealed class AccountsController(AccountService accounts, TransactionService transactions) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<AccountResponse>> List(CancellationToken cancellationToken) => accounts.ListAsync(cancellationToken);

    [HttpGet("{id:guid}")]
    public Task<AccountResponse> Get(Guid id, CancellationToken cancellationToken) => accounts.GetAsync(id, cancellationToken);

    [HttpPost]
    public Task<AccountResponse> Open(CancellationToken cancellationToken) => accounts.OpenAsync(cancellationToken);

    [HttpPost("{id:guid}/top-ups")]
    public Task<TransactionResponse> TopUp(Guid id, AmountRequest request,
        [FromHeader(Name = "Idempotency-Key")] Guid idempotencyKey, CancellationToken cancellationToken) =>
        transactions.TopUpAsync(id, request.Amount, idempotencyKey, cancellationToken);
}

public sealed record AmountRequest(decimal Amount);
