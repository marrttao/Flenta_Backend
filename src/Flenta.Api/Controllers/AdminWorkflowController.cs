using System.ComponentModel.DataAnnotations;
using Flenta.Application.Features.Admin;
using Flenta.Application.Features.Transactions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
namespace Flenta.Api.Controllers;

[ApiController, Authorize(Policy = "Admin"), Route("api/admin"), EnableRateLimiting("banking"), RequestSizeLimit(16384)]
public sealed class AdminWorkflowController(AdminWorkflowService service) : ControllerBase
{
    [HttpGet("clients/{id:guid}")] public Task<AdminClientDetail> Client(Guid id, CancellationToken ct) => service.ClientAsync(id, ct);
    [HttpGet("transactions")] public Task<AdminTransactionPage> Transactions(CancellationToken ct, string? query = null, string? status = null, int page = 1, DateOnly? date = null, string? kind = null) => service.TransactionsAsync(query, status, page, ct, date, kind);
    [HttpGet("kyc/{id:guid}/evidence")] public Task<AdminKycEvidence> Evidence(Guid id, CancellationToken ct) => service.EvidenceAsync(id, ct);
    [HttpGet("kyc")] public Task<AdminKycPage> Kyc(CancellationToken ct, string? status = null, int page = 1, string? query = null) => service.KycAsync(status, page, ct, query);
    [HttpPost("notifications"), EnableRateLimiting("auth")]
    public Task<bool> Notify(AdminNotificationRequest request, [FromHeader(Name = "Idempotency-Key")] Guid key, CancellationToken ct) =>
        service.NotifyAsync(request.OwnerId, request.Title, request.Body, request.Reason, request.Password, key, ct);
    [HttpPost("transactions/{id:guid}/actions"), EnableRateLimiting("auth")]
    public Task<bool> TransactionAction(Guid id, AdminTransactionActionRequest request, [FromHeader(Name = "Idempotency-Key")] Guid key, CancellationToken ct) =>
        service.TransactionActionAsync(id, request.Compensate, request.Reason, request.Password, key, ct);
    [HttpPost("clients/{id:guid}/controls"), EnableRateLimiting("auth")]
    public Task<AdminClientDetail> Controls(Guid id, AdminControlsRequest request, [FromHeader(Name = "Idempotency-Key")] Guid key, CancellationToken ct) =>
        service.ControlsAsync(id, request.Blocked, request.CardsFrozen, request.Reason, request.Password, key, ct);
    [HttpPost("clients/{id:guid}/adjustments"), EnableRateLimiting("auth")]
    public Task<TransactionResponse> Adjust(Guid id, AdminAdjustmentRequest request, [FromHeader(Name = "Idempotency-Key")] Guid key, CancellationToken ct) =>
        service.AdjustAsync(id, request.AccountId, request.Amount, request.Credit, request.Reason, request.Password, key, ct);
    [HttpPost("kyc/{id:guid}/reviews"), EnableRateLimiting("auth")]
    public Task<bool> Review(Guid id, AdminReviewRequest request, [FromHeader(Name = "Idempotency-Key")] Guid key, CancellationToken ct) =>
        service.ReviewAsync(id, request.Approved, request.Reason, request.Password, key, ct);
}
public sealed record AdminControlsRequest(bool Blocked, bool CardsFrozen, [Required, StringLength(1000, MinimumLength = 5)] string Reason, [Required, StringLength(128)] string Password);
public sealed record AdminAdjustmentRequest(Guid AccountId, decimal Amount, bool Credit, [Required, StringLength(1000, MinimumLength = 5)] string Reason, [Required, StringLength(128)] string Password);
public sealed record AdminReviewRequest(bool Approved, [Required, StringLength(1000, MinimumLength = 5)] string Reason, [Required, StringLength(128)] string Password);

public sealed record AdminTransactionActionRequest(bool Compensate, [Required, StringLength(1000, MinimumLength = 5)] string Reason, [Required, StringLength(128)] string Password);

public sealed record AdminNotificationRequest(Guid? OwnerId, [Required, StringLength(120)] string Title, [Required, StringLength(2000)] string Body, [Required, StringLength(1000, MinimumLength = 5)] string Reason, [Required, StringLength(128)] string Password);
