using System.Security.Cryptography;
using System.Text;
using Flenta.Application.Abstractions;
using Flenta.Application.Abstractions.Banking;
using Flenta.Application.Features.Accounts;
using Flenta.Application.Features.Auth;
using Flenta.Application.Features.Finance;
using Flenta.Domain.Entities;
using Flenta.Domain.Exceptions;
using Flenta.Domain.ValueObjects;

namespace Flenta.Application.Features.Transactions;

public sealed record TransactionResponse(Guid Id, string Kind, string Status, Guid? SourceAccountId, Guid? DestinationAccountId,
    string? DestinationIban, decimal Amount, decimal Fee, string Currency, DateTimeOffset CreatedAt, DateTimeOffset? FinishedAt, string? FailureCode, string? DestinationCurrency = null, decimal? ReceivedAmount = null,
    decimal? ExchangeRate = null, DateOnly? RateDate = null, decimal Cashback = 0, decimal Saved = 0, Guid? GoalId = null, string? SavingsSkipReason = null);
public sealed record TransferQuote(decimal Amount, decimal Fee, decimal Total, string Currency, bool SufficientFunds);

public sealed class TransactionService(IBankingRepository repository, IBankingUnitOfWork unitOfWork, ICurrentUser user, IClock clock, IFeePolicy fees)
{
    public static readonly Guid ClearingAccountId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    public static readonly Guid FeeAccountId = Guid.Parse("00000000-0000-0000-0000-000000000002");

    public async Task<TransferQuote> QuoteAsync(Guid sourceId, decimal amountValue, bool byIban, CancellationToken cancellationToken)
    {
        var amount = Money.FromDecimal(amountValue);
        amount.RequirePositive();
        var source = await repository.OwnedAccountAsync(user.Profile.Id, sourceId, cancellationToken) ?? throw AccountService.Missing();
        var fee = fees.Calculate(byIban ? TransactionKind.IbanTransfer : TransactionKind.InternalTransfer, amount);
        var total = amount.Add(fee);
        return new TransferQuote(amount.Amount, fee.Amount, total.Amount, source.CurrencyCode, source.BalanceMinorUnits >= total.MinorUnits);
    }

    public Task<TransactionResponse> TopUpAsync(Guid accountId, decimal amount, Guid key, CancellationToken cancellationToken) =>
        ExecuteAsync(TransactionKind.TopUp, accountId, null, null, amount, key, cancellationToken);

    public Task<TransactionResponse> TransferAsync(Guid sourceId, Guid destinationId, decimal amount, Guid key, CancellationToken cancellationToken) =>
        ExecuteAsync(TransactionKind.InternalTransfer, sourceId, destinationId, null, amount, key, cancellationToken);

    public Task<TransactionResponse> TransferByIbanAsync(Guid sourceId, string iban, decimal amount, Guid key, CancellationToken cancellationToken) =>
        ExecuteAsync(TransactionKind.IbanTransfer, sourceId, null, Iban.Parse(iban).Value, amount, key, cancellationToken);

    private Task<TransactionResponse> ExecuteAsync(TransactionKind kind, Guid customerAccountId, Guid? destinationId, string? iban,
        decimal amountValue, Guid key, CancellationToken cancellationToken)
    {
        if (key == Guid.Empty) throw new DomainValidationException("transaction.idempotency_key_required", "A non-empty Idempotency-Key UUID header is required.");
        var amount = Money.FromDecimal(amountValue);
        amount.RequirePositive();
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{kind}|{customerAccountId:N}|{destinationId:N}|{iban}|{amount.MinorUnits}")));
        return unitOfWork.ExecuteAsync(user.Profile.Id, key, async ct =>
        {
            var existing = await repository.ByKeyAsync(user.Profile.Id, key, ct);
            if (existing is not null)
            {
                if (existing.RequestFingerprint != fingerprint)
                    throw new ApplicationFailureException(FailureKind.Conflict, "transaction.key_reused", "This idempotency key was used with another request.");
                return ToResponse(existing);
            }

            var customer = await repository.OwnedAccountAsync(user.Profile.Id, customerAccountId, ct) ?? throw AccountService.Missing();
            if (iban is not null) destinationId = (await repository.AccountByIbanAsync(iban, ct))?.Id;
            var sourceId = kind == TransactionKind.TopUp ? SystemAccounts.Clearing(customer.CurrencyCode) : customerAccountId;
            var targetId = kind == TransactionKind.TopUp ? customerAccountId : destinationId;
            var fee = fees.Calculate(kind, amount);
            var ids = new[] { sourceId, targetId ?? Guid.Empty }
                .Concat(fee.MinorUnits > 0 ? new[] { SystemAccounts.Fees(customer.CurrencyCode) } : [])
                .Where(id => id != Guid.Empty).Distinct().ToArray();
            var locked = (await repository.LockAccountsAsync(ids, ct)).ToDictionary(a => a.Id);
            var storedTargetId = targetId is not null && locked.TryGetValue(targetId.Value, out var candidate) && candidate.Kind == AccountKind.Customer ? targetId : null;
            var operation = new BankTransaction(user.Profile.Id, key, fingerprint, kind, sourceId, storedTargetId, iban, amount, fee, clock.UtcNow, customer.CurrencyCode);
            repository.Add(operation);

            try
            {
                if (targetId is null || !locked.TryGetValue(targetId.Value, out var target) || target.Kind != AccountKind.Customer)
                    throw new DomainValidationException("transfer.recipient_not_found", "Recipient account does not exist in this sandbox.");
                if (sourceId == target.Id) throw new DomainValidationException("transfer.same_account", "Source and destination must be different accounts.");
                var source = locked[sourceId];
                if (source.CurrencyCode != target.CurrencyCode)
                    throw new DomainValidationException("transfer.currency_mismatch", "Use currency exchange for accounts in different currencies.");
                var revenue = fee.MinorUnits > 0 ? locked[SystemAccounts.Fees(customer.CurrencyCode)] : null;
                var total = amount.Add(fee);
                source.EnsureCanDebit(total);
                target.EnsureCanCredit(amount);
                if (fee.MinorUnits > 0) revenue!.EnsureCanCredit(fee);
                var entries = new List<LedgerEntry>
                {
                    new(operation.Id, source.Id, -total.MinorUnits, clock.UtcNow, source.CurrencyCode),
                    new(operation.Id, target.Id, amount.MinorUnits, clock.UtcNow, source.CurrencyCode)
                };
                if (fee.MinorUnits > 0) entries.Add(new LedgerEntry(operation.Id, revenue!.Id, fee.MinorUnits, clock.UtcNow, source.CurrencyCode));
                operation.Complete(entries, clock.UtcNow);
                source.Debit(total);
                target.Credit(amount);
                if (fee.MinorUnits > 0) revenue!.Credit(fee);
                repository.AddEntries(entries);
            }
            catch (DomainValidationException exception) when (operation.Status == TransactionStatus.Pending)
            {
                operation.Reject(exception.Code, clock.UtcNow);
            }
            return ToResponse(operation);
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<TransactionResponse>> ListAsync(int offset, int limit, CancellationToken cancellationToken)
    {
        if (offset is < 0 or > 10000 || limit is < 1 or > 200)
            throw new DomainValidationException("pagination.invalid", "Offset must be 0–10000 and limit 1–200.");
        return (await repository.TransactionsAsync(user.Profile.Id, offset, limit, cancellationToken)).Select(ToResponse).ToArray();
    }

    public async Task<TransactionResponse> GetAsync(Guid id, CancellationToken cancellationToken) =>
        ToResponse(await repository.OwnedTransactionAsync(user.Profile.Id, id, cancellationToken) ?? throw AccountService.Missing());

    public static TransactionResponse ToResponse(BankTransaction operation) => new(operation.Id, operation.Kind.ToString(), operation.Status.ToString(),
        operation.Kind == TransactionKind.TopUp ? null : operation.SourceAccountId, operation.DestinationAccountId, operation.DestinationIban,
        operation.AmountMinorUnits / 100m, operation.FeeMinorUnits / 100m, operation.CurrencyCode, operation.CreatedAt, operation.FinishedAt, operation.FailureCode,
        operation.DestinationCurrency, operation.ReceivedMinorUnits / 100m, operation.ExchangeRate, operation.RateDate,
        operation.CashbackMinorUnits / 100m, operation.SavedMinorUnits / 100m, operation.GoalId, operation.SavingsSkipReason);
}
