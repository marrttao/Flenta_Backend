using System.ComponentModel.DataAnnotations;
using Flenta.Api.Configuration;
using Flenta.Application.Features.Support;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
namespace Flenta.Api.Controllers;

[ApiController, Authorize(Policy = KycPolicies.Onboarding), Route("api/support/requests"), EnableRateLimiting("banking"), RequestSizeLimit(16384)]
public sealed class SupportController(SupportService service) : ControllerBase
{
    [HttpGet] public Task<IReadOnlyList<SupportSummary>> List(CancellationToken ct) => service.ListAsync(false, ct);
    [HttpGet("{id:guid}")] public Task<SupportConversation> Get(Guid id, CancellationToken ct) => service.GetAsync(id, false, ct);
    [HttpPost] public Task<SupportSummary> Create(CreateSupportRequest request, CancellationToken ct) => service.CreateAsync(request.Subject, request.Kind, request.Body, ct);
    [HttpPost("{id:guid}/messages")] public Task<bool> Reply(Guid id, ReplySupportRequest request, CancellationToken ct) => service.ReplyAsync(id, request.Body, false, ct);
    [HttpPost("{id:guid}/close")] public Task<bool> Close(Guid id, CancellationToken ct) => service.CloseAsync(id, false, ct);
}
[ApiController, Authorize(Policy = "Admin"), Route("api/admin/support"), EnableRateLimiting("banking"), RequestSizeLimit(16384)]
public sealed class AdminSupportController(SupportService service) : ControllerBase
{
    [HttpGet] public Task<IReadOnlyList<SupportSummary>> List(CancellationToken ct) => service.ListAsync(true, ct);
    [HttpGet("{id:guid}")] public Task<SupportConversation> Get(Guid id, CancellationToken ct) => service.GetAsync(id, true, ct);
    [HttpPost("{id:guid}/messages")] public Task<bool> Reply(Guid id, ReplySupportRequest request, CancellationToken ct) => service.ReplyAsync(id, request.Body, true, ct);
    [HttpPost("{id:guid}/close")] public Task<bool> Close(Guid id, CancellationToken ct) => service.CloseAsync(id, true, ct);
}
public sealed record CreateSupportRequest([Required, StringLength(120)] string Subject, [Required] string Kind, [Required, StringLength(4000)] string Body);
public sealed record ReplySupportRequest([Required, StringLength(4000)] string Body);
