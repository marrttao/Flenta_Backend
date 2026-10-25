using Flenta.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flenta.Infrastructure.Persistence.Configurations;

public sealed class VirtualCardConfiguration : IEntityTypeConfiguration<VirtualCard>
{
    public void Configure(EntityTypeBuilder<VirtualCard> card)
    {
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
    }
}
