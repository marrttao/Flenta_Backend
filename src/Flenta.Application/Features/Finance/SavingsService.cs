using Flenta.Application.Abstractions;
using Flenta.Application.Abstractions.Banking;
using Flenta.Application.Features.Accounts;
using Flenta.Application.Features.Auth;
using Flenta.Application.Features.Transactions;
using Flenta.Domain.Entities;
using Flenta.Domain.Exceptions;
using Flenta.Domain.ValueObjects;

namespace Flenta.Application.Features.Finance;

public sealed record GoalResponse(Guid Id, Guid FundingAccountId, string Name, string Currency, decimal Target,
    decimal Balance, decimal ProgressPercent, DateOnly? Deadline, string Mode, decimal Percentage, int RoundUpStep, bool Archived);
public sealed record GoalSettings(string Name, decimal Target, DateOnly? Deadline, string Mode, decimal Percentage, int RoundUpStep);

public sealed class SavingsService(IFinanceRepository finance, IBankingRepository bank, IBankingUnitOfWork unitOfWork, ICurrentUser user, IClock clock)
{
    public async Task<IReadOnlyList<GoalResponse>> ListAsync(CancellationToken ct)
    {
        var result = new List<GoalResponse>();
        foreach (var goal in await finance.GoalsAsync(user.Profile.Id, ct)) result.Add(await ResponseAsync(goal, ct));
        return result;
    }

    public Task<GoalResponse> CreateAsync(Guid fundingAccountId, string name, decimal target, DateOnly? deadline, CancellationToken ct) =>
        unitOfWork.ExecuteAsync(user.Profile.Id, Guid.NewGuid(), async token =>
        {
            var funding = await bank.OwnedAccountAsync(user.Profile.Id, fundingAccountId, token) ?? throw AccountService.Missing();
            var account = BankAccount.OpenSavings(user.Profile.Id, funding.CurrencyCode, clock.UtcNow);
            var goal = new SavingsGoal(user.Profile.Id, account.Id, funding.Id, name, Money.FromDecimal(target), deadline, clock.UtcNow);
            bank.Add(account); finance.Add(goal);
            return ToResponse(goal, account);
        }, ct);

    public Task<GoalResponse> UpdateAsync(Guid id, GoalSettings settings, CancellationToken ct) =>
        unitOfWork.ExecuteAsync(user.Profile.Id, Guid.NewGuid(), async token =>
        {
            var goal = await finance.GoalAsync(user.Profile.Id, id, true, token) ?? throw AccountService.Missing();
            if (!Enum.TryParse<SavingsMode>(settings.Mode, true, out var mode) || !Enum.IsDefined(mode))
                throw new DomainValidationException("goal.invalid_mode", "Use Off, Percentage or RoundUp.");
            goal.Update(settings.Name, Money.FromDecimal(settings.Target), settings.Deadline, mode, settings.Percentage, settings.RoundUpStep, clock.UtcNow);
            return await ResponseAsync(goal, token);
        }, ct);

    public Task<GoalResponse> ArchiveAsync(Guid id, CancellationToken ct) =>
        unitOfWork.ExecuteAsync(user.Profile.Id, Guid.NewGuid(), async token =>
        {
            var goal = await finance.GoalAsync(user.Profile.Id, id, true, token) ?? throw AccountService.Missing();
            var accounts = await bank.LockAccountsAsync([goal.AccountId], token);
            goal.Archive(accounts.Single().BalanceMinorUnits);
            return ToResponse(goal, accounts.Single());
        }, ct);

    public Task<TransactionResponse> MoveAsync(Guid id, decimal value, bool withdraw, Guid key, CancellationToken ct)
    {
        FinanceOperation.RequireKey(key);
        var amount = Money.FromDecimal(value); amount.RequirePositive();
        var kind = withdraw ? TransactionKind.SavingsWithdrawal : TransactionKind.SavingsDeposit;
        var fingerprint = FinanceOperation.Fingerprint($"{kind}|{id:N}|{amount.MinorUnits}");
        return unitOfWork.ExecuteAsync(user.Profile.Id, key, async token =>
        {
            var existing = await FinanceOperation.ExistingAsync(bank, user.Profile.Id, key, fingerprint, token);
            if (existing is not null) return existing;
            var goal = await finance.GoalAsync(user.Profile.Id, id, true, token) ?? throw AccountService.Missing();
            if (goal.Archived) throw new ApplicationFailureException(FailureKind.Conflict, "goal.archived", "The goal is archived.");
            var accounts = (await bank.LockAccountsAsync([goal.AccountId, goal.FundingAccountId], token)).ToDictionary(a => a.Id);
            var source = accounts[withdraw ? goal.AccountId : goal.FundingAccountId];
            var destination = accounts[withdraw ? goal.FundingAccountId : goal.AccountId];
            var operation = new BankTransaction(user.Profile.Id, key, fingerprint, kind, source.Id, destination.Id, null,
                amount, Money.FromMinorUnits(0), clock.UtcNow, source.CurrencyCode);
            operation.DescribeSavings(goal.Id);
            bank.Add(operation);
            try
            {
                bank.AddEntries(PostingPlan.Apply(operation, accounts, [(source.Id, -amount.MinorUnits), (destination.Id, amount.MinorUnits)], clock.UtcNow));
            }
            catch (DomainValidationException exception) when (operation.Status == TransactionStatus.Pending) { operation.Reject(exception.Code, clock.UtcNow); }
            return TransactionService.ToResponse(operation);
        }, ct);
    }

    private async Task<GoalResponse> ResponseAsync(SavingsGoal goal, CancellationToken ct) =>
        ToResponse(goal, await finance.SavingsAccountAsync(user.Profile.Id, goal.AccountId, ct) ?? throw AccountService.Missing());
    public static GoalResponse ToResponse(SavingsGoal goal, BankAccount account) => new(goal.Id, goal.FundingAccountId, goal.Name,
        account.CurrencyCode, goal.TargetMinorUnits / 100m, account.BalanceMinorUnits / 100m,
        decimal.Round(account.BalanceMinorUnits * 100m / goal.TargetMinorUnits, 2), goal.Deadline,
        goal.Mode.ToString(), goal.Percentage, goal.RoundUpStep, goal.Archived);
}
