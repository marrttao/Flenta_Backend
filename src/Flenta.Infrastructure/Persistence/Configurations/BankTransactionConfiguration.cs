using Flenta.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flenta.Infrastructure.Persistence.Configurations;

public sealed class BankTransactionConfiguration : IEntityTypeConfiguration<BankTransaction>
{
    public void Configure(EntityTypeBuilder<BankTransaction> transaction)
    {
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
    }
}
