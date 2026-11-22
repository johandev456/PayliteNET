using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using PaymentService.Clients;

namespace PaymentService.UnitTests;

public class ServiceTokenProviderTests
{
    /// <summary>Keycloak falso: cada petición emite un token nuevo (token-1, token-2...).</summary>
    private sealed class KeycloakFalso : HttpMessageHandler
    {
        public int Emitidos;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { access_token = $"token-{Interlocked.Increment(ref Emitidos)}", expires_in = 300 }),
            });
    }

    private sealed class Fabrica(HttpMessageHandler h) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(h, disposeHandler: false);
    }

    private static (ServiceTokenProvider, KeycloakFalso) Crear()
    {
        var kc = new KeycloakFalso();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ServiceClient:TokenEndpoint"] = "http://kc.test/token",
            ["ServiceClient:ClientId"] = "payment-service",
        }).Build();
        return (new ServiceTokenProvider(new Fabrica(kc), config, TimeProvider.System), kc);
    }

    [Fact]
    public async Task Cachea_el_token_mientras_esta_vigente()
    {
        var (p, kc) = Crear();
        Assert.Equal("token-1", await p.ObtenerAsync(default));
        Assert.Equal("token-1", await p.ObtenerAsync(default));
        Assert.Equal(1, kc.Emitidos);
    }

    [Fact]
    public async Task Invalidar_fuerza_un_token_nuevo()
    {
        var (p, kc) = Crear();
        var t1 = await p.ObtenerAsync(default);

        p.Invalidar(t1);

        Assert.Equal("token-2", await p.ObtenerAsync(default));
        Assert.Equal(2, kc.Emitidos);
    }

    [Fact]
    public async Task Invalidar_un_token_viejo_no_tira_el_nuevo()
    {
        var (p, kc) = Crear();
        var viejo = await p.ObtenerAsync(default);
        p.Invalidar(viejo);
        var nuevo = await p.ObtenerAsync(default);

        // Otro pago que usaba el token viejo informa el rechazo tarde: el nuevo se conserva.
        p.Invalidar(viejo);

        Assert.Equal(nuevo, await p.ObtenerAsync(default));
        Assert.Equal(2, kc.Emitidos);
    }
}
