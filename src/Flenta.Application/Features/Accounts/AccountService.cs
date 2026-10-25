using Flenta.Application.Abstractions;
using Flenta.Application.Abstractions.Banking;
using Flenta.Application.Features.Auth;
using Flenta.Domain.Entities;
using Flenta.Domain.ValueObjects;

namespace Flenta.Application.Features.Accounts;

public sealed record AccountResponse(Guid Id, string Iban, string Currency, decimal Balance, DateTimeOffset CreatedAt);

public sealed class AccountService(IBankingRepository repository, IBankingUnitOfWork unitOfWork, ICurrentUser user, IClock clock)
{
    public async Task<IReadOnlyList<AccountResponse>> ListAsync(CancellationToken cancellationToken) =>
        (await repository.AccountsAsync(user.Profile.Id, cancellationToken)).Select(ToResponse).ToArray();

    public async Task<AccountResponse> GetAsync(Guid id, CancellationToken cancellationToken) =>
        ToResponse(await repository.OwnedAccountAsync(user.Profile.Id, id, cancellationToken) ?? throw Missing());

    public Task<AccountResponse> OpenAsync(CancellationToken cancellationToken) =>
        unitOfWork.ExecuteAsync(user.Profile.Id, Guid.NewGuid(), _ =>
        {
            var account = BankAccount.Open(user.Profile.Id, clock.UtcNow);
            repository.Add(account);
            return Task.FromResult(ToResponse(account));
        }, cancellationToken);

    public static ApplicationFailureException Missing() => new(FailureKind.NotFound, "banking.not_found", "Resource not found.");
    public static AccountResponse ToResponse(BankAccount account) => new(account.Id, account.Iban!, Money.Currency, account.BalanceMinorUnits / 100m, account.CreatedAt);
}
