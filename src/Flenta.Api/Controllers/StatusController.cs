using Flenta.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace Flenta.Api.Controllers;

[ApiController]
[Route("api/status")]
public sealed class StatusController(IClock clock) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<ApiStatusResponse>(StatusCodes.Status200OK)]
    public ActionResult<ApiStatusResponse> Get()
    {
        return Ok(new ApiStatusResponse("Flenta API", "ok", clock.UtcNow));
    }
}

public sealed record ApiStatusResponse(string Service, string Status, DateTimeOffset Timestamp);
