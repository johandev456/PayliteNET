using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PaymentService.Infrastructure;

/// <summary>Solo para <c>dotnet ef</c>: genera migraciones sin arrancar la app ni una base viva.</summary>
public class DesignTimeFactory : IDesignTimeDbContextFactory<PaymentDbContext>
{
    public PaymentDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<PaymentDbContext>()
            .UseNpgsql("Host=localhost;Database=payments_design")
            .Options);
}
