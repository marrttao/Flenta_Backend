using Flenta.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flenta.Infrastructure.Persistence.Configurations;

public sealed class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> entry)
    {
        entry.ToTable("entries", table => table.HasCheckConstraint("ck_entry_nonzero", "\"AmountMinorUnits\" <> 0"));
        entry.HasKey(e => e.Id);
        entry.Property(e => e.Id).ValueGeneratedNever();
        entry.HasIndex(e => new { e.TransactionId, e.AccountId }).IsUnique();
        entry.HasOne<BankAccount>().WithMany().HasForeignKey(e => e.AccountId).OnDelete(DeleteBehavior.Restrict);
        entry.HasOne<BankTransaction>().WithMany().HasForeignKey(e => e.TransactionId).OnDelete(DeleteBehavior.Restrict);
    }
}
