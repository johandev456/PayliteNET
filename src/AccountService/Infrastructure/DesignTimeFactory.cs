using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AccountService.Infrastructure;

/// <summary>
/// Usada solo por <c>dotnet ef</c> para generar migraciones y scripts SQL
/// sin arrancar la aplicación ni necesitar una base viva.
/// </summary>
public class DesignTimeFactory : IDesignTimeDbContextFactory<AccountDbContext>
{
    public AccountDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AccountDbContext>()
            .UseNpgsql("Host=localhost;Database=accounts_design")
            .Options;
        return new AccountDbContext(options);
    }
}
