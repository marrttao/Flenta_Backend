using Flenta.Application.Abstractions.Banking;
using Flenta.Domain.Entities;
using Flenta.Domain.ValueObjects;

namespace Flenta.Application.Features.Transactions;

public sealed class SandboxFeePolicy(decimal ibanRate, Money minimumIbanFee) : IFeePolicy
{
    public Money Calculate(TransactionKind kind, Money amount)
    {
        if (kind != TransactionKind.IbanTransfer) return Money.FromMinorUnits(0);
        var fee = decimal.Round(amount.MinorUnits * ibanRate, 0, MidpointRounding.AwayFromZero);
        return Money.FromMinorUnits(Math.Max((long)fee, minimumIbanFee.MinorUnits));
    }
}
