using Flenta.Application.Abstractions.Banking;
using Flenta.Application.Features.Auth;
using Flenta.Domain.Entities;
using Flenta.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
namespace Flenta.Infrastructure.Persistence.Banking;

internal sealed class PostgresDesktopBankingRepository(BankingDbContext db, IOptions<BankingOptions> options) : ICardRoutingRepository, IStatementRepository
{
    public string Fingerprint(string number) => new SandboxCardVault(options).Fingerprint(number);
    public async Task<VirtualCard?> RecipientCardAsync(string fingerprint, CancellationToken ct) =>
        (await db.Cards.FromSqlInterpolated($"SELECT * FROM banking.cards WHERE \"Fingerprint\" = {fingerprint} FOR UPDATE").ToListAsync(ct)).SingleOrDefault();
    public async Task<IReadOnlyList<BankTransaction>> StatementAsync(Guid owner, DateTimeOffset from, DateTimeOffset until, CancellationToken ct)
    {
        if (!options.Value.Enabled) throw new ApplicationFailureException(FailureKind.Unavailable, "banking.not_configured", "Banking is unavailable.");
        try
        {
            return await db.Transactions.AsNoTracking().Where(t => t.CreatedAt >= from && t.CreatedAt < until && (t.InitiatorId == owner || t.Status == TransactionStatus.Completed && db.Accounts.Any(a => a.OwnerId == owner && (a.Id == t.SourceAccountId || a.Id == t.DestinationAccountId))))
                .OrderBy(t => t.CreatedAt).ThenBy(t => t.Id).Take(10001).ToListAsync(ct);
        }
        catch (Exception e) when (e is NpgsqlException or TimeoutException) { throw new ApplicationFailureException(FailureKind.Unavailable, "banking.storage_unavailable", "Statement storage is temporarily unavailable."); }
    }
}
