using Flenta.Application.Features.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
namespace Flenta.Api.Controllers;

[ApiController, Authorize, Route("api/notifications"), EnableRateLimiting("banking")]
public sealed class NotificationsController(NotificationService service) : ControllerBase
{
    [HttpGet] public Task<IReadOnlyList<NotificationResponse>> List(CancellationToken ct) => service.ListAsync(ct);
    [HttpPost("{id:guid}/read")] public Task<bool> Read(Guid id, CancellationToken ct) => service.ReadAsync(id, ct);
}
