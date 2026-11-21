using Flenta.Application.Abstractions.Kyc;
using Flenta.Application.Features.Profile;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
namespace Flenta.Api.Controllers;

[ApiController, Authorize, Route("api/profile/identity"), EnableRateLimiting("kyc")]
public sealed class VerifiedIdentityController(VerifiedIdentityService service) : ControllerBase
{
    [HttpGet] public Task<KycIdentityData?> Get(CancellationToken ct) => service.GetAsync(ct);
}
