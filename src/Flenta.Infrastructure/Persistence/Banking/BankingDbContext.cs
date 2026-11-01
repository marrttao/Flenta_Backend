using Flenta.Domain.Entities;
using Flenta.Application.Features.Finance;
using Flenta.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Flenta.Infrastructure.Persistence.Banking;

public sealed class BankingDbContext(DbContextOptions<BankingDbContext> options) : DbContext(options)
{
    public DbSet<BankAccount> Accounts => Set<BankAccount>();
    public DbSet<VirtualCard> Cards => Set<VirtualCard>();
    public DbSet<BankTransaction> Transactions => Set<BankTransaction>();
    public DbSet<LedgerEntry> Entries => Set<LedgerEntry>();

    public DbSet<SavingsGoal> Goals => Set<SavingsGoal>();
    public DbSet<ExchangeQuote> Quotes => Set<ExchangeQuote>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema("banking");
        var account = model.Entity<BankAccount>();
        account.ToTable("accounts", table =>
        {
            table.HasCheckConstraint("ck_account_currency", "\"CurrencyCode\" IN ('UAH', 'USD', 'EUR')");
            table.HasCheckConstraint("ck_account_balance", "\"Kind\" = 'SandboxClearing' OR \"BalanceMinorUnits\" >= 0");
            table.HasCheckConstraint("ck_account_owner", "(\"Kind\" = 'Customer' AND \"OwnerId\" IS NOT NULL AND \"Iban\" IS NOT NULL) OR (\"Kind\" = 'Savings' AND \"OwnerId\" IS NOT NULL AND \"Iban\" IS NULL) OR (\"Kind\" IN ('SandboxClearing', 'FeeRevenue', 'SandboxMerchant') AND \"OwnerId\" IS NULL AND \"Iban\" IS NULL)");
        });
        account.HasKey(a => a.Id);
        account.HasAlternateKey(a => new { a.Id, a.CurrencyCode });
        account.Property(a => a.CurrencyCode).HasMaxLength(3).HasDefaultValue("UAH");
        account.Property(a => a.Id).ValueGeneratedNever();
        account.Property(a => a.Kind).HasConversion<string>().HasMaxLength(32);
        account.Property(a => a.Iban).HasMaxLength(29);
        account.HasIndex(a => a.Iban).IsUnique();
        account.HasIndex(a => a.OwnerId);
        foreach (var currency in Currency.Supported)
        {
            account.HasData(new
            {
                Id = SystemAccounts.Clearing(currency), OwnerId = (Guid?)null, Iban = (string?)null,
                Kind = AccountKind.SandboxClearing, BalanceMinorUnits = 0L, CreatedAt = DateTimeOffset.UnixEpoch, CurrencyCode = currency
            }, new
            {
                Id = SystemAccounts.Fees(currency), OwnerId = (Guid?)null, Iban = (string?)null,
                Kind = AccountKind.FeeRevenue, BalanceMinorUnits = 0L, CreatedAt = DateTimeOffset.UnixEpoch, CurrencyCode = currency
            }, new
            {
                Id = SystemAccounts.Merchant(currency), OwnerId = (Guid?)null, Iban = (string?)null,
                Kind = AccountKind.SandboxMerchant, BalanceMinorUnits = 0L, CreatedAt = DateTimeOffset.UnixEpoch, CurrencyCode = currency
            });
        }

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
        transaction.Property(t => t.CurrencyCode).HasMaxLength(3).HasDefaultValue("UAH");
        transaction.Property(t => t.DestinationCurrency).HasMaxLength(3);
        transaction.Property(t => t.ExchangeRate).HasPrecision(28, 12);
        transaction.Property(t => t.SavingsSkipReason).HasMaxLength(100);
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
        entry.Property(e => e.CurrencyCode).HasMaxLength(3).HasDefaultValue("UAH");
        entry.HasKey(e => e.Id);
        entry.Property(e => e.Id).ValueGeneratedNever();
        entry.HasIndex(e => new { e.TransactionId, e.AccountId }).IsUnique();
        entry.HasOne<BankAccount>().WithMany().HasForeignKey(e => new { e.AccountId, e.CurrencyCode }).HasPrincipalKey(a => new { a.Id, a.CurrencyCode }).OnDelete(DeleteBehavior.Restrict);
        entry.HasOne<BankTransaction>().WithMany().HasForeignKey(e => e.TransactionId).OnDelete(DeleteBehavior.Restrict);

        var goal = model.Entity<SavingsGoal>();
        goal.ToTable("goals", table =>
        {
            table.HasCheckConstraint("ck_goal_settings", "\"TargetMinorUnits\" > 0 AND \"Percentage\" BETWEEN 0 AND 100 AND \"RoundUpStep\" IN (1,5,10,50,100) AND \"Mode\" IN ('Off','Percentage','RoundUp') AND (\"Mode\" <> 'Percentage' OR \"Percentage\" > 0)");
        });
        goal.HasKey(g => g.Id);
        goal.Property(g => g.Id).ValueGeneratedNever();
        goal.Property(g => g.Name).HasMaxLength(100);
        goal.Property(g => g.Mode).HasConversion<string>().HasMaxLength(16);
        goal.Property(g => g.Percentage).HasPrecision(5, 2);
        goal.HasIndex(g => g.OwnerId);
        goal.HasIndex(g => g.AccountId).IsUnique();
        goal.HasIndex(g => g.FundingAccountId).IsUnique().HasFilter("\"Mode\" <> 'Off' AND NOT \"Archived\"");
        goal.HasOne<BankAccount>().WithMany().HasForeignKey(g => g.AccountId).OnDelete(DeleteBehavior.Restrict);
        goal.HasOne<BankAccount>().WithMany().HasForeignKey(g => g.FundingAccountId).OnDelete(DeleteBehavior.Restrict);

        var quote = model.Entity<ExchangeQuote>();
        quote.ToTable("exchange_quotes", table => table.HasCheckConstraint("ck_exchange_quote", "\"AmountMinorUnits\" > 0 AND \"ReceivedMinorUnits\" > 0 AND \"Rate\" > 0 AND \"SourceCurrency\" <> \"DestinationCurrency\""));
        quote.HasKey(q => q.Id);
        quote.Property(q => q.Id).ValueGeneratedNever();
        quote.Property(q => q.SourceCurrency).HasMaxLength(3);
        quote.Property(q => q.DestinationCurrency).HasMaxLength(3);
        quote.Property(q => q.Rate).HasPrecision(28, 12);
        quote.HasIndex(q => q.OwnerId);
        quote.HasOne<BankAccount>().WithMany().HasForeignKey(q => q.SourceAccountId).OnDelete(DeleteBehavior.Restrict);
        quote.HasOne<BankAccount>().WithMany().HasForeignKey(q => q.DestinationAccountId).OnDelete(DeleteBehavior.Restrict);
        quote.HasOne<BankTransaction>().WithMany().HasForeignKey(q => q.TransactionId).OnDelete(DeleteBehavior.Restrict);
    }
}
