using System.ComponentModel.DataAnnotations;
using Flenta.Application.Features.Recipients;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
namespace Flenta.Api.Controllers;

[ApiController, Authorize, Route("api/recipients"), EnableRateLimiting("banking"), RequestSizeLimit(16384)]
public sealed class RecipientsController(RecipientService service) : ControllerBase
{
    [HttpGet] public Task<IReadOnlyList<RecipientResponse>> Get(CancellationToken ct) => service.ListAsync(ct);
    [HttpPost] public Task<RecipientResponse> Post(CreateRecipientRequest request, CancellationToken ct) => service.AddAsync(request.Name, request.Iban, ct);
    [HttpDelete("{id:guid}")] public Task<bool> Delete(Guid id, CancellationToken ct) => service.DeleteAsync(id, ct);
}
public sealed record CreateRecipientRequest([Required, StringLength(100)] string Name, [Required, StringLength(34)] string Iban);
