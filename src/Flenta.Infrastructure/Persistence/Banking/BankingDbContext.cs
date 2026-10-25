using Flenta.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Flenta.Infrastructure.Persistence.Banking;

public sealed class BankingDbContext(DbContextOptions<BankingDbContext> options) : DbContext(options)
{
    public DbSet<BankAccount> Accounts => Set<BankAccount>();
    public DbSet<VirtualCard> Cards => Set<VirtualCard>();
    public DbSet<BankTransaction> Transactions => Set<BankTransaction>();
    public DbSet<LedgerEntry> Entries => Set<LedgerEntry>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema("banking");
        var account = model.Entity<BankAccount>();
        account.ToTable("accounts", table =>
        {
            table.HasCheckConstraint("ck_account_balance", "\"Kind\" = 'SandboxClearing' OR \"BalanceMinorUnits\" >= 0");
            table.HasCheckConstraint("ck_account_owner", "(\"Kind\" = 'Customer' AND \"OwnerId\" IS NOT NULL AND \"Iban\" IS NOT NULL) OR (\"Kind\" IN ('SandboxClearing', 'FeeRevenue') AND \"OwnerId\" IS NULL AND \"Iban\" IS NULL)");
        });
        account.HasKey(a => a.Id);
        account.Property(a => a.Id).ValueGeneratedNever();
        account.Property(a => a.Kind).HasConversion<string>().HasMaxLength(32);
        account.Property(a => a.Iban).HasMaxLength(29);
        account.HasIndex(a => a.Iban).IsUnique();
        account.HasIndex(a => a.OwnerId);
        account.HasData(new
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000001"), OwnerId = (Guid?)null, Iban = (string?)null,
            Kind = AccountKind.SandboxClearing, BalanceMinorUnits = 0L, CreatedAt = DateTimeOffset.UnixEpoch
        }, new
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000002"), OwnerId = (Guid?)null, Iban = (string?)null,
            Kind = AccountKind.FeeRevenue, BalanceMinorUnits = 0L, CreatedAt = DateTimeOffset.UnixEpoch
        });

        var card = model.Entity<VirtualCard>();
        card.ToTable("cards", table =>
        {
            table.HasCheckConstraint("ck_card_status", "\"Status\" IN ('Active', 'Blocked', 'Closed')");
            table.HasCheckConstraint("ck_card_last4", "\"Last4\" ~ '^[0-9]{4}$'");
        });
        card.HasKey(c => c.Id);
        card.Property(c => c.Id).ValueGeneratedNever();
        card.Property(c => c.Last4).HasMaxLength(4);
        card.Property(c => c.ProtectedData).HasMaxLength(2048);
        card.Property(c => c.Fingerprint).HasMaxLength(64);
        card.Property(c => c.Status).HasConversion<string>().HasMaxLength(16);
        card.Ignore(c => c.MaskedNumber);
        card.HasIndex(c => c.Fingerprint).IsUnique();
        card.HasOne<BankAccount>().WithMany().HasForeignKey(c => c.AccountId).OnDelete(DeleteBehavior.Restrict);

        var transaction = model.Entity<BankTransaction>();
        transaction.ToTable("transactions", table =>
        {
            table.HasCheckConstraint("ck_transaction_amount", "\"AmountMinorUnits\" > 0 AND \"FeeMinorUnits\" >= 0");
            table.HasCheckConstraint("ck_transaction_status", "\"Status\" IN ('Pending', 'Completed', 'Rejected')");
        });
        transaction.HasKey(t => t.Id);
        transaction.Property(t => t.Id).ValueGeneratedNever();
        transaction.Property(t => t.Kind).HasConversion<string>().HasMaxLength(32);
        transaction.Property(t => t.Status).HasConversion<string>().HasMaxLength(16);
        transaction.Property(t => t.RequestFingerprint).HasMaxLength(64);
        transaction.Property(t => t.DestinationIban).HasMaxLength(29);
        transaction.Property(t => t.FailureCode).HasMaxLength(100);
        transaction.HasIndex(t => new { t.InitiatorId, t.IdempotencyKey }).IsUnique();
        transaction.HasIndex(t => new { t.InitiatorId, t.CreatedAt });
        transaction.HasIndex(t => t.DestinationAccountId);
        transaction.HasOne<BankAccount>().WithMany().HasForeignKey(t => t.SourceAccountId).OnDelete(DeleteBehavior.Restrict);
        transaction.HasOne<BankAccount>().WithMany().HasForeignKey(t => t.DestinationAccountId).OnDelete(DeleteBehavior.Restrict);

        var entry = model.Entity<LedgerEntry>();
        entry.ToTable("entries", table => table.HasCheckConstraint("ck_entry_nonzero", "\"AmountMinorUnits\" <> 0"));
        entry.HasKey(e => e.Id);
        entry.Property(e => e.Id).ValueGeneratedNever();
        entry.HasIndex(e => new { e.TransactionId, e.AccountId }).IsUnique();
        entry.HasOne<BankAccount>().WithMany().HasForeignKey(e => e.AccountId).OnDelete(DeleteBehavior.Restrict);
        entry.HasOne<BankTransaction>().WithMany().HasForeignKey(e => e.TransactionId).OnDelete(DeleteBehavior.Restrict);
    }
}
