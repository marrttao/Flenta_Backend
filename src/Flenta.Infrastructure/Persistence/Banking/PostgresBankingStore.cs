using System.Buffers.Binary;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using Flenta.Application.Abstractions.Banking;
using Flenta.Application.Features.Auth;
using Flenta.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Flenta.Infrastructure.Persistence.Banking;

internal sealed class PostgresBankingStore(AppDbContext db, IOptions<BankingOptions> options) : IBankingRepository, IBankingUnitOfWork
{
    private void EnsureConfigured()
    {
        if (!options.Value.Enabled) throw new ApplicationFailureException(FailureKind.Unavailable, "banking.not_configured", "Banking service is unavailable.");
    }

    private async Task<T> ReadAsync<T>(Func<Task<T>> read)
    {
        EnsureConfigured();
        try { return await read(); }
        catch (Exception exception) when (exception is NpgsqlException or TimeoutException)
        {
            throw new ApplicationFailureException(FailureKind.Unavailable, "banking.storage_unavailable", "Banking storage is unavailable. Try again later.");
        }
    }

    public async Task<T> ExecuteAsync<T>(Guid owner, Guid operationKey, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{owner:N}:{operationKey:N}"));
            var lockId = BinaryPrimitives.ReadInt64BigEndian(hash);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockId})", cancellationToken);
            var result = await action(cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ApplicationFailureException(FailureKind.Conflict, "banking.conflict", "A conflicting resource already exists. Retry with a new request.");
        }
        catch (Exception exception) when (exception is NpgsqlException or DbUpdateException or TimeoutException)
        {
            throw new ApplicationFailureException(FailureKind.Unavailable, "banking.storage_unavailable", "Banking storage is unavailable. Retry using the same idempotency key.");
        }
    }

    public async Task<IReadOnlyList<BankAccount>> AccountsAsync(Guid owner, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        return await ReadAsync(() => db.Accounts.AsNoTracking().Where(a => a.OwnerId == owner && a.Kind == AccountKind.Customer).OrderBy(a => a.CreatedAt).ThenBy(a => a.Id).ToListAsync(cancellationToken));
    }

    public Task<BankAccount?> OwnedAccountAsync(Guid owner, Guid accountId, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        return ReadAsync(() => db.Accounts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == accountId && a.OwnerId == owner && a.Kind == AccountKind.Customer, cancellationToken));
    }

    public Task<BankAccount?> AccountByIbanAsync(string iban, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        return ReadAsync(() => db.Accounts.AsNoTracking().SingleOrDefaultAsync(a => a.Iban == iban && a.Kind == AccountKind.Customer, cancellationToken));
    }

    public async Task<IReadOnlyList<BankAccount>> LockAccountsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken) =>
        await db.Accounts.FromSqlInterpolated($"SELECT * FROM banking.accounts WHERE \"Id\" = ANY({ids.ToArray()}) ORDER BY \"Id\" FOR UPDATE")
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<VirtualCard>> CardsAsync(Guid owner, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        return await ReadAsync(() => db.Cards.AsNoTracking().Where(c => db.Accounts.Any(a => a.Id == c.AccountId && a.OwnerId == owner))
            .OrderBy(c => c.CreatedAt).ThenBy(c => c.Id).ToListAsync(cancellationToken));
    }

    public async Task<VirtualCard?> OwnedCardAsync(Guid owner, Guid cardId, bool forUpdate, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        if (!forUpdate) return await ReadAsync(() => db.Cards.AsNoTracking().SingleOrDefaultAsync(c => c.Id == cardId && db.Accounts.Any(a => a.Id == c.AccountId && a.OwnerId == owner), cancellationToken));
        var cards = await db.Cards.FromSqlInterpolated($"SELECT c.* FROM banking.cards c JOIN banking.accounts a ON a.\"Id\" = c.\"AccountId\" WHERE c.\"Id\" = {cardId} AND a.\"OwnerId\" = {owner} FOR UPDATE OF c")
            .ToListAsync(cancellationToken);
        return cards.SingleOrDefault();
    }

    public Task<BankTransaction?> ByKeyAsync(Guid owner, Guid key, CancellationToken cancellationToken) =>
        db.Transactions.AsNoTracking().SingleOrDefaultAsync(t => t.InitiatorId == owner && t.IdempotencyKey == key, cancellationToken);

    private IQueryable<BankTransaction> VisibleTransactions(Guid owner) => db.Transactions.AsNoTracking().Where(t => t.InitiatorId == owner
        || t.Status == TransactionStatus.Completed && db.Accounts.Any(a => a.Id == t.DestinationAccountId && a.OwnerId == owner));

    public async Task<IReadOnlyList<BankTransaction>> TransactionsAsync(Guid owner, int offset, int limit, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        return await ReadAsync(() => VisibleTransactions(owner).OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id).Skip(offset).Take(limit).ToListAsync(cancellationToken));
    }

    public Task<BankTransaction?> OwnedTransactionAsync(Guid owner, Guid transactionId, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        return ReadAsync(() => VisibleTransactions(owner).SingleOrDefaultAsync(t => t.Id == transactionId, cancellationToken));
    }

    public void Add(BankAccount account) => db.Accounts.Add(account);
    public void Add(VirtualCard card) => db.Cards.Add(card);
    public void Add(BankTransaction transaction) => db.Transactions.Add(transaction);
    public void AddEntries(IEnumerable<LedgerEntry> entries) => db.Entries.AddRange(entries);
}
