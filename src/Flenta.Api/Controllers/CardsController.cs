using System.ComponentModel.DataAnnotations;
using Flenta.Application.Features.Cards;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Flenta.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/cards")]
[EnableRateLimiting("banking")]
[RequestSizeLimit(16 * 1024)]
public sealed class CardsController(CardService cards) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<CardResponse>> List(CancellationToken cancellationToken) => cards.ListAsync(cancellationToken);

    [HttpGet("{id:guid}")]
    public Task<CardResponse> Get(Guid id, CancellationToken cancellationToken) => cards.GetAsync(id, cancellationToken);

    [HttpPost]
    public Task<CardResponse> Create(CreateCardRequest request, CancellationToken cancellationToken) => cards.CreateAsync(request.AccountId, cancellationToken);

    [HttpPost("{id:guid}/block")]
    public Task<CardResponse> Block(Guid id, CancellationToken cancellationToken) => cards.ChangeStatusAsync(id, "block", cancellationToken);

    [HttpPost("{id:guid}/unblock")]
    public Task<CardResponse> Unblock(Guid id, CancellationToken cancellationToken) => cards.ChangeStatusAsync(id, "unblock", cancellationToken);

    [HttpPost("{id:guid}/close")]
    public Task<CardResponse> Close(Guid id, CancellationToken cancellationToken) => cards.ChangeStatusAsync(id, "close", cancellationToken);

    [HttpPost("{id:guid}/requisites")]
    [EnableRateLimiting("auth")]
    public Task<CardSecretsResponse> Requisites(Guid id, ConfirmCardRequest request, CancellationToken cancellationToken) => cards.RevealAsync(id, request.Password, cancellationToken);
}

public sealed record CreateCardRequest(Guid AccountId);
public sealed record ConfirmCardRequest([Required, StringLength(128)] string Password);
