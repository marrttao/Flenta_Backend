using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Flenta.Infrastructure.Persistence.Banking;

public sealed class BankingDbContextFactory : IDesignTimeDbContextFactory<BankingDbContext>
{
    public BankingDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("Banking__ConnectionString");
        return new BankingDbContext(new DbContextOptionsBuilder<BankingDbContext>().UseNpgsql(connection).Options);
    }
}
