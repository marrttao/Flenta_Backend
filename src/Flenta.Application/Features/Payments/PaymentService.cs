using Flenta.Application.Abstractions;
using Flenta.Application.Abstractions.Banking;
using Flenta.Application.Features.Accounts;
using Flenta.Application.Features.Finance;
using Flenta.Application.Features.Transactions;
using Flenta.Domain.Entities;
using Flenta.Domain.Exceptions;
using Flenta.Domain.ValueObjects;
namespace Flenta.Application.Features.Payments;

public sealed class PaymentService(IBankingRepository bank, IBankingUnitOfWork work, ICurrentUser user, IClock clock, ICardRoutingRepository cards)
{
    public Task<TransactionResponse> PayAsync(Guid accountId, decimal value, string kind, string reference, Guid key, CancellationToken ct)
    {
        if (kind is not ("MobileTopUp" or "LoanRepayment")) throw new DomainValidationException("payment.invalid_kind", "Unsupported sandbox payment.");
        var text = reference.Trim();
        if (kind == "MobileTopUp" ? text.Length is < 8 or > 16 || text[0] != '+' || text.Skip(1).Any(c => !char.IsAsciiDigit(c)) : text.Length is < 4 or > 50 || text.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
            throw new DomainValidationException("payment.invalid_reference", "Enter an international phone number or loan reference.");
        return ExecuteAsync(accountId, null, value, Enum.Parse<TransactionKind>(kind), text, key, ct);
    }
    public Task<TransactionResponse> CardTransferAsync(Guid accountId, string number, decimal value, string? memo, Guid key, CancellationToken ct)
    {
        var fingerprint = cards.Fingerprint(number);
        return ExecuteAsync(accountId, fingerprint, value, TransactionKind.CardTransfer, memo?.Trim() ?? "", key, ct);
    }
    private Task<TransactionResponse> ExecuteAsync(Guid accountId, string? cardFingerprint, decimal value, TransactionKind kind, string text, Guid key, CancellationToken ct)
    {
        FinanceOperation.RequireKey(key); var amount = Money.FromDecimal(value); amount.RequirePositive();
        if (text.Length > 280 || text.Any(char.IsControl)) throw new DomainValidationException("transaction.invalid_memo", "Use a message of up to 280 characters.");
        var fingerprint = FinanceOperation.Fingerprint($"{kind}|{accountId:N}|{cardFingerprint}|{amount.MinorUnits}|{text}");
        return work.ExecuteAsync(user.Profile.Id, key, async token =>
        {
            var existing = await FinanceOperation.ExistingAsync(bank, user.Profile.Id, key, fingerprint, token); if (existing is not null) return existing;
            var source = await bank.OwnedAccountAsync(user.Profile.Id, accountId, token) ?? throw AccountService.Missing();
            VirtualCard? card = null;
            if (cardFingerprint is not null) card = await cards.RecipientCardAsync(cardFingerprint, token);
            var destination = card?.AccountId ?? SystemAccounts.Merchant(source.CurrencyCode);
            var accounts = (await bank.LockAccountsAsync([source.Id, destination], token)).ToDictionary(a => a.Id);
            var op = new BankTransaction(user.Profile.Id, key, fingerprint, kind, source.Id, card is null ? null : destination, null, amount, Money.FromMinorUnits(0), clock.UtcNow, source.CurrencyCode);
            op.DescribeMemo(kind == TransactionKind.CardTransfer ? text : $"{kind} · …{text[^4..]}"); bank.Add(op);
            try
            {
                if (cardFingerprint is not null)
                {
                    if (card is null) throw new DomainValidationException("transfer.recipient_not_found", "Recipient card is unavailable in this sandbox.");
                    card.EnsureUsable(clock.UtcNow);
                    if (source.Id == destination) throw new DomainValidationException("transfer.same_account", "Choose a different recipient account.");
                    if (accounts[destination].CurrencyCode != source.CurrencyCode) throw new DomainValidationException("transfer.currency_mismatch", "The recipient account must use the same currency.");
                }
                bank.AddEntries(PostingPlan.Apply(op, accounts, [(source.Id, -amount.MinorUnits), (destination, amount.MinorUnits)], clock.UtcNow));
            }
            catch (DomainValidationException e) when (op.Status == TransactionStatus.Pending) { op.Reject(e.Code, clock.UtcNow); }
            return TransactionService.ToResponse(op);
        }, ct);
    }
}
