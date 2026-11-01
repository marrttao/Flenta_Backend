using System.ComponentModel.DataAnnotations;
using Flenta.Application.Features.Finance;
using Flenta.Application.Features.Transactions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Flenta.Api.Controllers;

[ApiController, Authorize, Route("api/goals"), EnableRateLimiting("banking"), RequestSizeLimit(16 * 1024)]
public sealed class GoalsController(SavingsService savings) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<GoalResponse>> List(CancellationToken ct) => savings.ListAsync(ct);

    [HttpPost]
    public Task<GoalResponse> Create(CreateGoalRequest request, CancellationToken ct) =>
        savings.CreateAsync(request.FundingAccountId, request.Name, request.Target, request.Deadline, ct);

    [HttpPut("{id:guid}")]
    public Task<GoalResponse> Update(Guid id, GoalSettings request, CancellationToken ct) => savings.UpdateAsync(id, request, ct);

    [HttpPost("{id:guid}/archive")]
    public Task<GoalResponse> Archive(Guid id, CancellationToken ct) => savings.ArchiveAsync(id, ct);

    [HttpPost("{id:guid}/deposits")]
    public Task<TransactionResponse> Deposit(Guid id, AmountRequest request, [FromHeader(Name = "Idempotency-Key")] Guid key, CancellationToken ct) =>
        savings.MoveAsync(id, request.Amount, false, key, ct);

    [HttpPost("{id:guid}/withdrawals")]
    public Task<TransactionResponse> Withdraw(Guid id, AmountRequest request, [FromHeader(Name = "Idempotency-Key")] Guid key, CancellationToken ct) =>
        savings.MoveAsync(id, request.Amount, true, key, ct);
}

public sealed record CreateGoalRequest(Guid FundingAccountId, [Required, StringLength(100)] string Name, decimal Target, DateOnly? Deadline);
