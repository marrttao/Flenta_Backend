using Flenta.Domain.Entities;
namespace Flenta.Application.Abstractions.Banking;

public interface ICardRoutingRepository
{
    string Fingerprint(string number);
    Task<VirtualCard?> RecipientCardAsync(string fingerprint, CancellationToken ct);
}
public interface IStatementRepository
{
    Task<IReadOnlyList<BankTransaction>> StatementAsync(Guid owner, DateTimeOffset from, DateTimeOffset until, CancellationToken ct);
}
