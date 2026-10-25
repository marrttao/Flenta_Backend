using Flenta.Domain.Entities;
using Flenta.Domain.ValueObjects;

namespace Flenta.Application.Abstractions.Banking;

public interface IFeePolicy
{
    Money Calculate(TransactionKind kind, Money amount);
}
