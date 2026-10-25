using Flenta.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flenta.Infrastructure.Persistence.Configurations;

public sealed class BankAccountConfiguration : IEntityTypeConfiguration<BankAccount>
{
    public void Configure(EntityTypeBuilder<BankAccount> account)
    {
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
    }
}
