using Flenta.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Flenta.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<BankAccount> Accounts => Set<BankAccount>();
    public DbSet<VirtualCard> Cards => Set<VirtualCard>();
    public DbSet<BankTransaction> Transactions => Set<BankTransaction>();
    public DbSet<LedgerEntry> Entries => Set<LedgerEntry>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        base.OnModelCreating(model);
        model.HasDefaultSchema("banking");
        model.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}