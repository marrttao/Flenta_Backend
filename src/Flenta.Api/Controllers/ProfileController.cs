using System.ComponentModel.DataAnnotations;
using Flenta.Application.Features.Auth;
using Flenta.Application.Features.Profile;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flenta.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/profile")]
[RequestSizeLimit(16 * 1024)]
public sealed class ProfileController(ProfileService profiles) : ControllerBase
{
    [HttpGet]
    public ProfileResponse Get() => profiles.GetCurrent();

    [HttpPut]
    public Task<ProfileResponse> Update(UpdateProfileRequest request, CancellationToken cancellationToken) =>
        profiles.UpdateCurrentAsync(request.DisplayName, cancellationToken);
}

public sealed record UpdateProfileRequest([Required, StringLength(100)] string DisplayName);
