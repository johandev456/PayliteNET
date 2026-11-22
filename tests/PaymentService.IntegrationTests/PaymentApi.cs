using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PayLite.Testing;
using PaymentService.Clients;

namespace PaymentService.IntegrationTests;

[CollectionDefinition(Nombre)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Nombre = "postgres";
}

/// <summary>
/// PaymentService real contra Postgres real; AccountService se reemplaza por <see cref="CuentasFalsas"/>
/// en el HttpMessageHandler primario. El resilience handler (timeouts, reintentos) sigue activo.
/// </summary>
public sealed class PaymentApi(string conexion, CuentasFalsas cuentas) : WebApplicationFactory<Program>
{
    public const string TokenServicio = "token-de-servicio";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Payments", conexion);
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("AccountService:BaseUrl", "http://accounts.test/");
        builder.UseSetting("AccountService:AttemptTimeout", "00:00:00.500");
        builder.UseSetting("AccountService:TotalTimeout", "00:00:02");
        builder.UseSetting("AccountService:MaxRetries", "1");
        // La recuperación se dispara a mano en los tests (RecuperarAsync), sin esperas.
        builder.UseSetting("Recuperacion:Enabled", "false");
        builder.UseSetting("Recuperacion:EsperaMinima", "00:00:00");
        builder.UseSetting("Recuperacion:MaxIntentos", "3");
        TestAuth.Configurar(builder);

        builder.ConfigureTestServices(s =>
        {
            s.AddHttpClient<AccountClient>().ConfigurePrimaryHttpMessageHandler(() => cuentas);
            s.RemoveAll<IServiceTokenProvider>();
            s.AddSingleton<IServiceTokenProvider>(new TokenFijo());
        });
    }

    private sealed class TokenFijo : IServiceTokenProvider
    {
        public Task<string> ObtenerAsync(CancellationToken ct) => Task.FromResult(TokenServicio);
    }
}

public sealed record LlamadaDebito(string Ruta, string? IdempotencyKey, string? Token, decimal Monto, string? OwnerId);

/// <summary>
/// AccountService falso con idempotencia real por Idempotency-Key, para comprobar
/// qué recibe y cuántas veces "cobra". <see cref="Modo"/> simula fallos.
/// </summary>
public sealed class CuentasFalsas : HttpMessageHandler
{
    public enum Falla { Ninguna, SaldoInsuficiente, CuentaNoExiste, Caido, Lento, Error500 }

    public Falla Modo { get; set; }
    public ConcurrentQueue<LlamadaDebito> Llamadas { get; } = new();
    public ConcurrentDictionary<string, decimal> Cobrados { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        // ReadAsString deja el contenido en buffer: el resilience handler reenvía el mismo request al reintentar.
        var cuerpo = JsonDocument.Parse(await req.Content!.ReadAsStringAsync(ct)).RootElement;
        var key = req.Headers.TryGetValues("Idempotency-Key", out var v) ? v.Single() : null;
        Llamadas.Enqueue(new LlamadaDebito(
            req.RequestUri!.AbsolutePath, key, req.Headers.Authorization?.Parameter,
            cuerpo.GetProperty("monto").GetDecimal(),
            cuerpo.TryGetProperty("ownerId", out var o) ? o.GetString() : null));

        switch (Modo)
        {
            case Falla.Caido:
                throw new HttpRequestException("Connection refused");
            case Falla.Lento:
                await Task.Delay(TimeSpan.FromSeconds(10), ct);
                break;
            case Falla.Error500:
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            case Falla.SaldoInsuficiente:
                return Problema(HttpStatusCode.UnprocessableEntity, "Saldo insuficiente");
            case Falla.CuentaNoExiste:
                return Problema(HttpStatusCode.NotFound, "Cuenta no encontrada");
        }

        Cobrados.TryAdd(key!, cuerpo.GetProperty("monto").GetDecimal());
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { ok = true }) };
    }

    private static HttpResponseMessage Problema(HttpStatusCode status, string titulo) =>
        new(status) { Content = JsonContent.Create(new { title = titulo, status = (int)status }) };
}
