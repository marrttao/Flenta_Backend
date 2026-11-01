using Flenta.Application.Abstractions;
using Flenta.Application.Abstractions.Banking;
using Flenta.Application.Features.Accounts;
using Flenta.Application.Features.Transactions;
using Flenta.Domain.Entities;
using Flenta.Domain.Exceptions;
using Flenta.Domain.ValueObjects;

namespace Flenta.Application.Features.Finance;

public sealed record CashbackResponse(decimal Percentage, IReadOnlyList<TransactionResponse> Transactions);

public sealed class PurchaseService(IFinanceRepository finance, IBankingRepository bank, IBankingUnitOfWork unitOfWork, ICurrentUser user, IClock clock)
{
    public const decimal CashbackPercentage = 1m;
    public async Task<CashbackResponse> CashbackAsync(int offset, int limit, CancellationToken ct)
    {
        if (offset is < 0 or > 10000 || limit is < 1 or > 200)
            throw new DomainValidationException("pagination.invalid", "Offset must be 0–10000 and limit 1–200.");
        return new CashbackResponse(CashbackPercentage, (await finance.CashbackAsync(user.Profile.Id, offset, limit, ct)).Select(TransactionService.ToResponse).ToArray());
    }

    public Task<TransactionResponse> PayAsync(Guid cardId, decimal value, Guid key, CancellationToken ct)
    {
        FinanceOperation.RequireKey(key);
        var amount = Money.FromDecimal(value); amount.RequirePositive();
        var fingerprint = FinanceOperation.Fingerprint($"Purchase|{cardId:N}|{amount.MinorUnits}");
        return unitOfWork.ExecuteAsync(user.Profile.Id, key, async token =>
        {
            var existing = await FinanceOperation.ExistingAsync(bank, user.Profile.Id, key, fingerprint, token);
            if (existing is not null) return existing;
            var card = await bank.OwnedCardAsync(user.Profile.Id, cardId, true, token) ?? throw AccountService.Missing();
            var source = await bank.OwnedAccountAsync(user.Profile.Id, card.AccountId, token) ?? throw AccountService.Missing();
            var goal = await finance.AutomaticGoalAsync(user.Profile.Id, source.Id, token);
            var ids = new[] { source.Id, SystemAccounts.Merchant(source.CurrencyCode), SystemAccounts.Clearing(source.CurrencyCode) }
                .Concat(goal is null ? [] : new[] { goal.AccountId }).ToArray();
            var accounts = (await bank.LockAccountsAsync(ids, token)).ToDictionary(a => a.Id);
            source = accounts[source.Id];
            var operation = new BankTransaction(user.Profile.Id, key, fingerprint, TransactionKind.Purchase, source.Id,
                SystemAccounts.Merchant(source.CurrencyCode), null, amount, Money.FromMinorUnits(0), clock.UtcNow, source.CurrencyCode);
            bank.Add(operation);
            try
            {
                card.EnsureUsable(clock.UtcNow);
                source.EnsureCanDebit(amount);
                var cashback = Money.FromMinorUnits((long)decimal.Round(amount.MinorUnits * CashbackPercentage / 100m, 0, MidpointRounding.AwayFromZero));
                var savings = goal?.Contribution(amount, accounts[goal.AccountId].BalanceMinorUnits) ?? Money.FromMinorUnits(0);
                string? skipReason = null;
                if (savings.MinorUnits > 0 && source.BalanceMinorUnits - amount.MinorUnits < savings.MinorUnits)
                { savings = Money.FromMinorUnits(0); skipReason = "insufficient_funds"; }
                var changes = new List<(Guid, long)>
                {
                    (source.Id, -amount.MinorUnits - savings.MinorUnits + cashback.MinorUnits),
                    (SystemAccounts.Merchant(source.CurrencyCode), amount.MinorUnits),
                    (SystemAccounts.Clearing(source.CurrencyCode), -cashback.MinorUnits)
                };
                if (savings.MinorUnits > 0) changes.Add((goal!.AccountId, savings.MinorUnits));
                operation.DescribePurchase(cashback, savings, goal?.Id, skipReason);
                bank.AddEntries(PostingPlan.Apply(operation, accounts, changes, clock.UtcNow));
            }
            catch (DomainValidationException exception) when (operation.Status == TransactionStatus.Pending) { operation.Reject(exception.Code, clock.UtcNow); }
            return TransactionService.ToResponse(operation);
        }, ct);
    }
}
