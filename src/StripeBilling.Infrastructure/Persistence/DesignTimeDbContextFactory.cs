using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace StripeBilling.Infrastructure.Persistence;

/// <summary>Used only by `dotnet ef` to generate SQL Server migrations.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<BillingDbContext>
{
    public BillingDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<BillingDbContext>()
            .UseSqlServer("Server=localhost;Database=StripeBilling;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;
        return new BillingDbContext(options);
    }
}
