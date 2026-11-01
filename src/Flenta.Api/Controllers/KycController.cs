using Flenta.Api.Configuration;
using Flenta.Application.Abstractions.Kyc;
using Flenta.Application.Features.Auth;
using Flenta.Application.Features.Kyc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Flenta.Api.Controllers;

[ApiController, Authorize(Policy = KycPolicies.Onboarding), Route("api/kyc"), EnableRateLimiting("kyc"), RequestSizeLimit(64 * 1024)]
public sealed class KycController(KycService kyc, IKycWebhookAuthenticator authenticator) : ControllerBase
{
    [HttpGet]
    public Task<KycStatusResponse> Status(CancellationToken ct) => kyc.GetAsync(ct);

    [HttpPost("session")]
    public Task<KycSessionResponse> Session(CancellationToken ct) => kyc.SessionAsync(ct);

    [HttpPost("refresh")]
    public Task<KycStatusResponse> Refresh(CancellationToken ct) => kyc.RefreshAsync(ct);

    [AllowAnonymous, HttpPost("webhooks/didit"), DisableRateLimiting]
    public async Task<IActionResult> DiditWebhook(CancellationToken ct)
    {
        using var memory = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await Request.Body.ReadAsync(buffer, ct)) > 0)
        {
            if (memory.Length + read > 64 * 1024) return StatusCode(StatusCodes.Status413PayloadTooLarge);
            memory.Write(buffer, 0, read);
        }
        var bytes = memory.ToArray();
        if (Request.Headers["X-Signature"].Count > 1 || Request.Headers["X-Signature-V2"].Count > 1 || Request.Headers["X-Timestamp"].Count != 1
            || !authenticator.Verify(bytes, Request.Headers["X-Signature"].ToString(), Request.Headers["X-Signature-V2"].ToString(), Request.Headers["X-Timestamp"].ToString()))
            throw new ApplicationFailureException(FailureKind.Unauthorized, "kyc.webhook_invalid", "Webhook authentication failed.");
        var notification = authenticator.ReadNotification(bytes);
        if (notification is not null) await kyc.ReceiveWebhookAsync(notification, ct);
        return Ok();
    }
}
