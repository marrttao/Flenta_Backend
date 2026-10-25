using Flenta.Domain.Entities;

namespace Flenta.Application.Abstractions.Banking;

public interface IBankingRepository
{
    Task<IReadOnlyList<BankAccount>> AccountsAsync(Guid owner, CancellationToken cancellationToken);
    Task<BankAccount?> OwnedAccountAsync(Guid owner, Guid accountId, CancellationToken cancellationToken);
    Task<BankAccount?> AccountByIbanAsync(string iban, CancellationToken cancellationToken);
    Task<IReadOnlyList<BankAccount>> LockAccountsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken);
    Task<IReadOnlyList<VirtualCard>> CardsAsync(Guid owner, CancellationToken cancellationToken);
    Task<VirtualCard?> OwnedCardAsync(Guid owner, Guid cardId, bool forUpdate, CancellationToken cancellationToken);
    Task<BankTransaction?> ByKeyAsync(Guid owner, Guid key, CancellationToken cancellationToken);
    Task<IReadOnlyList<BankTransaction>> TransactionsAsync(Guid owner, int offset, int limit, CancellationToken cancellationToken);
    Task<BankTransaction?> OwnedTransactionAsync(Guid owner, Guid transactionId, CancellationToken cancellationToken);
    void Add(BankAccount account);
    void Add(VirtualCard card);
    void Add(BankTransaction transaction);
    void AddEntries(IEnumerable<LedgerEntry> entries);
}

public interface IBankingUnitOfWork
{
    Task<T> ExecuteAsync<T>(Guid owner, Guid operationKey, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken);
}
