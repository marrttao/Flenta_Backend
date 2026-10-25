using Flenta.Domain.Exceptions;
using Flenta.Domain.ValueObjects;

namespace Flenta.Domain.Entities;

public enum TransactionKind { TopUp, InternalTransfer, IbanTransfer }
public enum TransactionStatus { Pending, Completed, Rejected }

public sealed class BankTransaction : Entity
{
    private BankTransaction() : base(Guid.NewGuid()) { }
    public BankTransaction(Guid initiatorId, Guid idempotencyKey, string fingerprint, TransactionKind kind, Guid sourceAccountId,
        Guid? destinationAccountId, string? destinationIban, Money amount, Money fee, DateTimeOffset now) : base(Guid.NewGuid())
    {
        amount.RequirePositive();
        InitiatorId = initiatorId;
        IdempotencyKey = idempotencyKey;
        RequestFingerprint = fingerprint;
        Kind = kind;
        SourceAccountId = sourceAccountId;
        DestinationAccountId = destinationAccountId;
        DestinationIban = destinationIban;
        AmountMinorUnits = amount.MinorUnits;
        FeeMinorUnits = fee.MinorUnits;
        CreatedAt = now;
    }

    public Guid InitiatorId { get; private set; }
    public Guid IdempotencyKey { get; private set; }
    public string RequestFingerprint { get; private set; } = "";
    public TransactionKind Kind { get; private set; }
    public TransactionStatus Status { get; private set; }
    public Guid SourceAccountId { get; private set; }
    public Guid? DestinationAccountId { get; private set; }
    public string? DestinationIban { get; private set; }
    public long AmountMinorUnits { get; private set; }
    public long FeeMinorUnits { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }
    public string? FailureCode { get; private set; }

    public void Complete(IReadOnlyList<LedgerEntry> entries, DateTimeOffset now)
    {
        EnsurePending();
        if (entries.Count < 2 || entries.Any(e => e.TransactionId != Id || e.AmountMinorUnits == 0)
            || entries.Select(e => e.AccountId).Distinct().Count() != entries.Count
            || entries.Sum(e => (decimal)e.AmountMinorUnits) != 0)
            throw new DomainValidationException("ledger.unbalanced", "Posting entries must balance to zero for this transaction.");
        Status = TransactionStatus.Completed;
        FinishedAt = now;
    }

    public void Reject(string code, DateTimeOffset now)
    {
        EnsurePending();
        FailureCode = code;
        Status = TransactionStatus.Rejected;
        FinishedAt = now;
    }

    private void EnsurePending()
    {
        if (Status != TransactionStatus.Pending) throw new DomainValidationException("transaction.finalized", "A finalized transaction cannot change status.");
    }
}

public sealed class LedgerEntry : Entity
{
    private LedgerEntry() : base(Guid.NewGuid()) { }
    public LedgerEntry(Guid transactionId, Guid accountId, long amountMinorUnits, DateTimeOffset now) : base(Guid.NewGuid())
    {
        TransactionId = transactionId;
        AccountId = accountId;
        AmountMinorUnits = amountMinorUnits;
        CreatedAt = now;
    }
    public Guid TransactionId { get; private set; }
    public Guid AccountId { get; private set; }
    public long AmountMinorUnits { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
