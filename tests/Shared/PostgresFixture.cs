using Npgsql;
using Testcontainers.PostgreSql;

namespace PayLite.Testing;

/// <summary>
/// PostgreSQL real para los tests de integración (no H2/InMemory: NUMERIC, CHECK, xmin y UNIQUE
/// tienen que comportarse como en producción).
/// <list type="bullet">
/// <item>Por defecto levanta un contenedor con Testcontainers (requiere Docker).</item>
/// <item>Si existe <c>PAYLITE_TEST_POSTGRES</c> (cadena de conexión a un Postgres con permiso CREATEDB),
/// usa ese servidor. Útil en CI sin Docker.</item>
/// </list>
/// Cada llamada a <see cref="NuevaBase"/> devuelve una base vacía distinta: los tests no se pisan.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _contenedor;
    private string _baseConexion = "";

    public async Task InitializeAsync()
    {
        var externo = Environment.GetEnvironmentVariable("PAYLITE_TEST_POSTGRES");
        if (!string.IsNullOrWhiteSpace(externo))
        {
            _baseConexion = externo;
            return;
        }

        _contenedor = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await _contenedor.StartAsync();
        _baseConexion = _contenedor.GetConnectionString();
    }

    /// <summary>Cadena de conexión a una base nueva; la crea la migración de la app al arrancar.</summary>
    public string NuevaBase(string prefijo) =>
        new NpgsqlConnectionStringBuilder(_baseConexion)
        {
            Database = $"{prefijo}_{Guid.NewGuid():N}",
            // Pools separados por base; sin esto los tests paralelos agotan conexiones rápido.
            MaxPoolSize = 20,
        }.ConnectionString;

    public async Task DisposeAsync()
    {
        if (_contenedor is not null)
            await _contenedor.DisposeAsync();
    }
}
