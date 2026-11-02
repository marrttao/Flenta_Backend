using Flenta.Domain.Exceptions;
using Flenta.Domain.ValueObjects;

namespace Flenta.Domain.Entities;

public enum SavingsMode { Off, Percentage, RoundUp }

public sealed class SavingsGoal : Entity
{
    private SavingsGoal() : base(Guid.NewGuid()) { }
    public SavingsGoal(Guid ownerId, Guid accountId, Guid fundingAccountId, string name, Money target,
        DateOnly? deadline, DateTimeOffset now) : base(Guid.NewGuid())
    {
        OwnerId = ownerId;
        AccountId = accountId;
        FundingAccountId = fundingAccountId;
        CreatedAt = now;
        Update(name, target, deadline, SavingsMode.Off, 0, 10, now);
    }

    public Guid OwnerId { get; private set; }
    public Guid AccountId { get; private set; }
    public Guid FundingAccountId { get; private set; }
    public string Name { get; private set; } = "";
    public long TargetMinorUnits { get; private set; }
    public DateOnly? Deadline { get; private set; }
    public SavingsMode Mode { get; private set; }
    public decimal Percentage { get; private set; }
    public int RoundUpStep { get; private set; }
    public bool Archived { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void Update(string name, Money target, DateOnly? deadline, SavingsMode mode, decimal percentage, int roundUpStep, DateTimeOffset now)
    {
        if (Archived) throw new DomainValidationException("goal.archived", "An archived goal cannot be changed.");
        target.RequirePositive();
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100 || !Enum.IsDefined(mode)
            || percentage is < 0 or > 100 || decimal.Round(percentage, 2) != percentage
            || mode == SavingsMode.Percentage && percentage == 0
            || roundUpStep is not (1 or 5 or 10 or 50 or 100)
            || deadline < DateOnly.FromDateTime(now.UtcDateTime) && deadline != Deadline)
            throw new DomainValidationException("goal.invalid", "Check name, target date, percentage (0.01–100) and rounding step (1, 5, 10, 50, 100).");
        Name = name.Trim(); TargetMinorUnits = target.MinorUnits; Deadline = deadline;
        Mode = mode; Percentage = percentage; RoundUpStep = roundUpStep;
    }

    public Money Contribution(Money expense, long balance)
    {
        if (Archived || Mode == SavingsMode.Off || balance >= TargetMinorUnits) return Money.FromMinorUnits(0);
        var amount = Mode == SavingsMode.Percentage
            ? (long)decimal.Round(expense.MinorUnits * Percentage / 100m, 0, MidpointRounding.AwayFromZero)
            : (RoundUpStep * 100L - expense.MinorUnits % (RoundUpStep * 100L)) % (RoundUpStep * 100L);
        return Money.FromMinorUnits(Math.Min(amount, TargetMinorUnits - balance));
    }

    public void Archive(long balance)
    {
        if (balance != 0) throw new DomainValidationException("goal.not_empty", "Withdraw savings before archiving the goal.");
        Archived = true; Mode = SavingsMode.Off;
    }
}
