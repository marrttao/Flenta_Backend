using Flenta.Domain.Entities;
using Flenta.Domain.Exceptions;
using Flenta.Domain.ValueObjects;

namespace Flenta.Tests.Banking;

public sealed class FinanceDomainTests
{
    [Theory]
    [InlineData(15640, 360)]
    [InlineData(16000, 0)]
    [InlineData(1, 999)]
    public void Roundup_moves_only_the_difference_to_the_next_step(long amount, long expected)
    {
        var now = DateTimeOffset.UtcNow;
        var goal = new SavingsGoal(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Goal", Money.FromDecimal(1000), null, now);
        goal.Update("Goal", Money.FromDecimal(1000), null, SavingsMode.RoundUp, 0, 10, now);
        Assert.Equal(expected, goal.Contribution(Money.FromMinorUnits(amount), 0).MinorUnits);
    }

    [Fact]
    public void Custom_percentage_rounds_half_kopecks_and_caps_at_remaining_target()
    {
        var now = DateTimeOffset.UtcNow;
        var goal = new SavingsGoal(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Goal", Money.FromDecimal(100), null, now);
        goal.Update("Goal", Money.FromDecimal(100), null, SavingsMode.Percentage, 12.5m, 10, now);
        Assert.Equal(13, goal.Contribution(Money.FromDecimal(1), 0).MinorUnits);
        Assert.Equal(5, goal.Contribution(Money.FromDecimal(1), 9995).MinorUnits);
        Assert.Equal(0, goal.Contribution(Money.FromDecimal(1), 10000).MinorUnits);
    }

    [Fact]
    public void Global_zero_is_not_sufficient_for_a_multicurrency_journal()
    {
        var now = DateTimeOffset.UtcNow;
        var op = new BankTransaction(Guid.NewGuid(), Guid.NewGuid(), "test", TransactionKind.Exchange, Guid.NewGuid(), Guid.NewGuid(), null,
            Money.FromDecimal(1), Money.FromDecimal(0), now);
        Assert.Throws<DomainValidationException>(() => op.Complete([
            new(op.Id, Guid.NewGuid(), -100, now, "UAH"), new(op.Id, Guid.NewGuid(), 50, now, "UAH"),
            new(op.Id, Guid.NewGuid(), -50, now, "USD"), new(op.Id, Guid.NewGuid(), 100, now, "USD")
        ], now));
    }
}
