using Flenta.Application.Abstractions;
using Flenta.Application.Abstractions.Banking;
using Flenta.Application.Features.Accounts;
using Flenta.Application.Features.Auth;
using Flenta.Domain.Entities;
using Flenta.Domain.Exceptions;

namespace Flenta.Application.Features.Cards;

public sealed record CardResponse(Guid Id, Guid AccountId, string MaskedNumber, string Status, int ExpiryMonth, int ExpiryYear, bool Sandbox, DateTimeOffset? LastSecretsViewedAt, bool OnlinePayments = true, bool ContactlessPayments = true, bool CashWithdrawals = true, decimal MonthlyLimit = 10000000000m, bool AdminFrozen = false);
public sealed record CardSecretsResponse(string Number, string Cvv, int ExpiryMonth, int ExpiryYear, bool Sandbox);

public sealed class CardService(IBankingRepository repository, IBankingUnitOfWork unitOfWork, ICurrentUser user,
    IClock clock, ICardVault vault, IIdentityProvider identityProvider, Flenta.Application.Abstractions.Admin.IAdminWorkflowRepository controls)
{
    public async Task<IReadOnlyList<CardResponse>> ListAsync(CancellationToken cancellationToken) =>
        (await repository.CardsAsync(user.Profile.Id, cancellationToken)).Select(ToResponse).ToArray();

    public async Task<CardResponse> GetAsync(Guid id, CancellationToken cancellationToken) =>
        ToResponse(await repository.OwnedCardAsync(user.Profile.Id, id, false, cancellationToken) ?? throw AccountService.Missing());

    public Task<CardResponse> CreateAsync(Guid accountId, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteAsync(user.Profile.Id, Guid.NewGuid(), async ct =>
        {
            var control = await controls.ControlAsync(user.Profile.Id, true, ct);
            if (control?.Blocked == true || control?.CardsFrozen == true)
                throw new ApplicationFailureException(FailureKind.Forbidden, "card.restricted", "Card creation is restricted. Contact support.");
            _ = await repository.OwnedAccountAsync(user.Profile.Id, accountId, ct) ?? throw AccountService.Missing();
            var id = Guid.NewGuid();
            var prepared = vault.Create(user.Profile.Id, id);
            var expiry = clock.UtcNow.AddYears(3);
            var card = new VirtualCard(id, accountId, prepared.Last4, prepared.Ciphertext, prepared.Fingerprint, expiry.Month, expiry.Year, clock.UtcNow);
            repository.Add(card);
            return ToResponse(card);
        }, cancellationToken);

    public Task<CardResponse> ChangeStatusAsync(Guid id, string action, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteAsync(user.Profile.Id, Guid.NewGuid(), async ct =>
        {
            var card = await repository.OwnedCardAsync(user.Profile.Id, id, true, ct) ?? throw AccountService.Missing();
            switch (action)
            {
                case "block": card.Block(); break;
                case "unblock": card.Unblock(); break;
                case "close": card.Close(); break;
                default: throw new DomainValidationException("card.invalid_action", "Unsupported card action.");
            }
            return ToResponse(card);
        }, cancellationToken);

    public Task<CardResponse> ConfigureAsync(Guid id, bool online, bool contactless, bool cash, decimal limit, CancellationToken ct) =>
        unitOfWork.ExecuteAsync(user.Profile.Id, Guid.NewGuid(), async token =>
        {
            var card = await repository.OwnedCardAsync(user.Profile.Id, id, true, token) ?? throw AccountService.Missing();
            card.Configure(online, contactless, cash, limit); return ToResponse(card);
        }, ct);

    public async Task<CardSecretsResponse> RevealAsync(Guid id, string password, CancellationToken cancellationToken)
    {
        _ = await repository.OwnedCardAsync(user.Profile.Id, id, false, cancellationToken) ?? throw AccountService.Missing();
        if (string.IsNullOrEmpty(password) || password.Length > 128)
            throw new DomainValidationException("auth.password_required", "Confirm your password to view requisites.");
        AuthSession confirmation;
        try
        {
            confirmation = await identityProvider.LoginAsync(user.Profile.Email.Value, password, cancellationToken);
        }
        catch (ApplicationFailureException exception) when (exception.Kind == FailureKind.Unauthorized)
        {
            // A failed password confirmation does not invalidate the caller's authenticated session.
            throw new ApplicationFailureException(FailureKind.Forbidden, "card.confirmation_failed", "Password confirmation failed.");
        }
        // This temporary login must not leave an additional refresh session behind.
        await identityProvider.LogoutAsync(confirmation.AccessToken, cancellationToken);
        if (confirmation.User.Id != user.Profile.Id)
            throw new ApplicationFailureException(FailureKind.Forbidden, "card.confirmation_failed", "Password confirmation failed.");
        return await unitOfWork.ExecuteAsync(user.Profile.Id, Guid.NewGuid(), async ct =>
        {
            var card = await repository.OwnedCardAsync(user.Profile.Id, id, true, ct) ?? throw AccountService.Missing();
            card.MarkSecretsViewed(clock.UtcNow);
            var secrets = vault.Reveal(user.Profile.Id, card.Id, card.ProtectedData);
            return new CardSecretsResponse(secrets.Number, secrets.Cvv, card.ExpiryMonth, card.ExpiryYear, true);
        }, cancellationToken);
    }

    public static CardResponse ToResponse(VirtualCard card) => new(card.Id, card.AccountId, card.MaskedNumber, card.Status.ToString(), card.ExpiryMonth, card.ExpiryYear, true, card.LastSecretsViewedAt, card.OnlinePayments, card.ContactlessPayments, card.CashWithdrawals, card.MonthlyLimitMinorUnits / 100m, card.AdminFrozen);
}
