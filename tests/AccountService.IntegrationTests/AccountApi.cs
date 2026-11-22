using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using PayLite.Testing;

namespace AccountService.IntegrationTests;

[CollectionDefinition(Nombre)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Nombre = "postgres";
}

/// <summary>AccountService real (migraciones, EF, auth, ProblemDetails) contra una base nueva.</summary>
public sealed class AccountApi(string conexion) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Accounts", conexion);
        builder.UseSetting("Database:MigrateOnStartup", "true");
        TestAuth.Configurar(builder);
    }
}
