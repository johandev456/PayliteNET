using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace PaymentService.Clients;

public interface IServiceTokenProvider
{
    Task<string> ObtenerAsync(CancellationToken ct);

    /// <summary>Descarta el token cacheado si sigue siendo <paramref name="token"/> (AccountService lo rechazó).</summary>
    void Invalidar(string token);
}

/// <summary>
/// Token de servicio por client credentials (client confidencial <c>payment-service</c> en Keycloak,
/// con el rol SERVICIO). Singleton: el token se cachea hasta poco antes de vencer.
/// </summary>
public sealed class ServiceTokenProvider(IHttpClientFactory clientes, IConfiguration config, TimeProvider reloj) : IServiceTokenProvider
{
    public const string NombreCliente = "keycloak";

    private static readonly TimeSpan Margen = TimeSpan.FromSeconds(30);
    private readonly SemaphoreSlim _lock = new(1, 1);
    // Token y vencimiento en un solo objeto inmutable: un lector nunca ve uno nuevo con el otro viejo.
    private TokenCacheado? _cache;

    public async Task<string> ObtenerAsync(CancellationToken ct)
    {
        if (Vigente() is { } vigente)
            return vigente;

        await _lock.WaitAsync(ct);
        try
        {
            if (Vigente() is { } otro)
                return otro;

            var cfg = config.GetSection("ServiceClient");
            var http = clientes.CreateClient(NombreCliente);
            using var resp = await http.PostAsync(cfg["TokenEndpoint"], new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = cfg["ClientId"] ?? "",
                ["client_secret"] = cfg["ClientSecret"] ?? "",
            }), ct);
            resp.EnsureSuccessStatusCode();

            var cuerpo = await resp.Content.ReadFromJsonAsync<RespuestaToken>(ct)
                         ?? throw new InvalidOperationException("Respuesta de token vacía.");
            Volatile.Write(ref _cache,
                new TokenCacheado(cuerpo.AccessToken, reloj.GetUtcNow().AddSeconds(cuerpo.ExpiresIn) - Margen));
            return cuerpo.AccessToken;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Invalidar(string token)
    {
        var actual = Volatile.Read(ref _cache);
        // Solo si nadie lo renovó ya: no tirar un token nuevo por el rechazo de uno viejo.
        if (actual?.Token == token)
            Interlocked.CompareExchange(ref _cache, null, actual);
    }

    private string? Vigente() =>
        Volatile.Read(ref _cache) is { } c && reloj.GetUtcNow() < c.Vence ? c.Token : null;

    private sealed record TokenCacheado(string Token, DateTimeOffset Vence);

    private sealed record RespuestaToken(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
