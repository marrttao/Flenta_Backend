using System.ComponentModel.DataAnnotations;
using Flenta.Application.Features.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
namespace Flenta.Api.Controllers;

[ApiController, Authorize, Route("api/settings"), EnableRateLimiting("banking"), RequestSizeLimit(16384)]
public sealed class SettingsController(SettingsService service) : ControllerBase
{
    [HttpGet("avatar")] public async Task<object> Avatar(CancellationToken ct) => new { dataUrl = await service.AvatarAsync(ct) };
    [HttpPut("avatar"), RequestSizeLimit(360000)] public Task<bool> Avatar(AvatarRequest request, CancellationToken ct) => service.SetAvatarAsync(request.Base64, ct);
    [HttpGet] public Task<SettingsResponse> Get(CancellationToken ct) => service.GetAsync(ct);
    [HttpPut] public Task<SettingsResponse> Put(UpdateSettingsRequest request, CancellationToken ct) => service.UpdateAsync(request.Phone, request.DefaultCurrency, request.TransactionNotifications, ct);
}
public sealed record UpdateSettingsRequest([StringLength(16)] string? Phone, [Required, StringLength(3)] string DefaultCurrency, bool TransactionNotifications);

public sealed record AvatarRequest([StringLength(350000)] string? Base64);
