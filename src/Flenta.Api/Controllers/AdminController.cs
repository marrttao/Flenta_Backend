using Flenta.Application.Features.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Flenta.Api.Controllers;

[ApiController, Authorize(Policy = "Admin"), Route("api/admin"), EnableRateLimiting("banking")]
public sealed class AdminController(AdminService admin) : ControllerBase
{
    [HttpGet("access")]
    public object Access()
    {
        admin.RequireAccess();
        return new { canAccess = true };
    }

    [HttpGet("overview")]
    public Task<AdminOverviewResponse> Overview(CancellationToken ct) => admin.OverviewAsync(ct);

    [HttpGet("clients")]
    public Task<AdminClientsResponse> Clients(CancellationToken ct, [FromQuery] string? email = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 8) => admin.ClientsAsync(email, page, pageSize, ct);
}
