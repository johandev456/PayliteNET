using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace PaymentService.Clients;

public interface IServiceTokenProvider
{
    Task<string> ObtenerAsync(CancellationToken ct);
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
    private string? _token;
    private DateTimeOffset _vence;

    public async Task<string> ObtenerAsync(CancellationToken ct)
    {
        if (_token is not null && reloj.GetUtcNow() < _vence)
            return _token;

        await _lock.WaitAsync(ct);
        try
        {
            if (_token is not null && reloj.GetUtcNow() < _vence)
                return _token;

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
            _token = cuerpo.AccessToken;
            _vence = reloj.GetUtcNow().AddSeconds(cuerpo.ExpiresIn) - Margen;
            return _token;
        }
        finally
        {
            _lock.Release();
        }
    }

    private sealed record RespuestaToken(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
