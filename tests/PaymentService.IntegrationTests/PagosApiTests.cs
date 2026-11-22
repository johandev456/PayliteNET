using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PayLite.Testing;
using PaymentService.Recovery;
using static PaymentService.IntegrationTests.CuentasFalsas;

namespace PaymentService.IntegrationTests;

[Collection(PostgresCollection.Nombre)]
public sealed class PagosApiTests(PostgresFixture pg) : IAsyncLifetime
{
    private readonly CuentasFalsas _cuentas = new();
    private PaymentApi _api = null!;
    private static readonly Guid Cuenta = Guid.NewGuid();
    private static readonly string TokenUser1 = TestAuth.Token("user-1");

    public Task InitializeAsync()
    {
        _api = new PaymentApi(pg.NuevaBase("pay"), _cuentas);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    private Task<HttpResponseMessage> PagarAsync(decimal monto, string metodo = "TARJETA", string? key = "k-1",
        string token = "", Guid? cuenta = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/pagos")
        {
            Content = JsonContent.Create(new { cuentaId = cuenta ?? Cuenta, monto, metodo }),
        };
        if (key is not null) req.Headers.Add("Idempotency-Key", key);
        return _api.CreateClient().Como(token == "" ? TokenUser1 : token).SendAsync(req);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    private async Task<string> EstadoAsync(Guid id) =>
        (await _api.CreateClient().Como(TokenUser1).GetFromJsonAsync<JsonElement>($"/api/pagos/{id}"))
        .GetProperty("estado").GetString()!;

    private Task<int> RecuperarAsync() =>
        _api.Services.GetRequiredService<RecuperacionPagosService>().RecuperarAsync(CancellationToken.None);

    [Fact]
    public async Task Pago_aprobado_debita_monto_mas_comision_con_el_token_del_usuario()
    {
        var resp = await PagarAsync(100m);
        var pago = await Json(resp);

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        Assert.Equal("APROBADO", pago.GetProperty("estado").GetString());
        Assert.Equal(2.90m, pago.GetProperty("comision").GetDecimal());

        var llamada = Assert.Single(_cuentas.Llamadas);
        Assert.Equal($"/api/cuentas/{Cuenta}/debito", llamada.Ruta);
        Assert.Equal(102.90m, llamada.Monto);
        Assert.Equal(TokenUser1, llamada.Token);
        // El id del pago es la clave de la operación en AccountService: lo que hace seguro reintentar.
        Assert.Equal(pago.GetProperty("id").GetGuid().ToString(), llamada.IdempotencyKey);
    }

    [Fact]
    public async Task Repetir_con_la_misma_clave_devuelve_el_mismo_pago_sin_volver_a_cobrar()
    {
        var r1 = await PagarAsync(100m);
        var r2 = await PagarAsync(100m);

        Assert.Equal(HttpStatusCode.Created, r1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
        Assert.Equal("true", r2.Headers.GetValues("Idempotent-Replayed").Single());
        Assert.Equal((await Json(r1)).GetProperty("id").GetGuid(), (await Json(r2)).GetProperty("id").GetGuid());
        Assert.Single(_cuentas.Llamadas);
    }

    [Fact]
    public async Task La_misma_clave_es_por_usuario()
    {
        await PagarAsync(100m, key: "compartida");
        var otro = await PagarAsync(100m, key: "compartida", token: TestAuth.Token("user-2"));

        Assert.Equal(HttpStatusCode.Created, otro.StatusCode);
        Assert.Equal(2, _cuentas.Llamadas.Count);
    }

    [Fact]
    public async Task Misma_clave_con_otros_datos_409()
    {
        await PagarAsync(100m);
        Assert.Equal(HttpStatusCode.Conflict, (await PagarAsync(200m)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await PagarAsync(100m, "BILLETERA")).StatusCode);
        Assert.Single(_cuentas.Llamadas);
    }

    [Fact]
    public async Task Pagos_simultaneos_con_la_misma_clave_crean_un_solo_pago()
    {
        var respuestas = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => PagarAsync(100m, key: "carrera")));

        Assert.Single(respuestas, r => r.StatusCode == HttpStatusCode.Created);
        Assert.All(respuestas, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK }));
        Assert.Single(respuestas.Select(r => Json(r).Result.GetProperty("id").GetGuid()).Distinct());
        Assert.Single(_cuentas.Llamadas);
    }

    [Theory]
    [InlineData(Falla.SaldoInsuficiente, "Saldo insuficiente")]
    [InlineData(Falla.CuentaNoExiste, "Cuenta no encontrada")]
    public async Task Rechazo_de_account_service_es_201_rechazado(Falla falla, string motivo)
    {
        _cuentas.Modo = falla;
        var resp = await PagarAsync(100m);
        var pago = await Json(resp);

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        Assert.Equal("RECHAZADO", pago.GetProperty("estado").GetString());
        Assert.Equal(motivo, pago.GetProperty("motivoRechazo").GetString());
    }

    [Theory]
    [InlineData("TARJETA", 50_000.01)]
    [InlineData("BILLETERA", 20_000.01)]
    [InlineData("TRANSFERENCIA", 500_000.01)]
    public async Task Sobre_el_limite_422_sin_llamar_a_cuentas(string metodo, double monto)
    {
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await PagarAsync((decimal)monto, metodo)).StatusCode);
        Assert.Empty(_cuentas.Llamadas);
    }

    [Theory]
    [InlineData(10, "CHEQUE", "k")]
    [InlineData(0, "TARJETA", "k")]
    [InlineData(-1, "TARJETA", "k")]
    [InlineData(10, "TARJETA", null)]
    public async Task Entrada_invalida_400(double monto, string metodo, string? key)
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await PagarAsync((decimal)monto, metodo, key)).StatusCode);
        Assert.Empty(_cuentas.Llamadas);
    }

    [Theory]
    [InlineData(Falla.Caido, HttpStatusCode.ServiceUnavailable)]
    [InlineData(Falla.Lento, HttpStatusCode.GatewayTimeout)]
    [InlineData(Falla.Error500, HttpStatusCode.BadGateway)]
    public async Task Fallo_de_account_service_deja_el_pago_pendiente(Falla falla, HttpStatusCode esperado)
    {
        _cuentas.Modo = falla;
        var resp = await PagarAsync(100m);
        var problema = await Json(resp);

        Assert.Equal(esperado, resp.StatusCode);
        var pagoId = problema.GetProperty("pagoId").GetGuid();
        Assert.Equal("PENDIENTE", await EstadoAsync(pagoId));
        // El resilience handler reintenta (seguro: misma Idempotency-Key en cada intento).
        Assert.True(_cuentas.Llamadas.Count >= 2);
        Assert.All(_cuentas.Llamadas, l => Assert.Equal(pagoId.ToString(), l.IdempotencyKey));
    }

    [Fact]
    public async Task Recuperacion_reenvia_el_debito_con_token_de_servicio_y_el_mismo_id()
    {
        _cuentas.Modo = Falla.Caido;
        var pagoId = (await Json(await PagarAsync(100m))).GetProperty("pagoId").GetGuid();

        _cuentas.Modo = Falla.Ninguna;
        Assert.Equal(1, await RecuperarAsync());

        Assert.Equal("APROBADO", await EstadoAsync(pagoId));
        var ultima = _cuentas.Llamadas.Last();
        Assert.Equal($"/api/internal/cuentas/{Cuenta}/debito", ultima.Ruta);
        Assert.Equal(PaymentApi.TokenServicio, ultima.Token);
        Assert.Equal("user-1", ultima.OwnerId);
        Assert.Equal(pagoId.ToString(), ultima.IdempotencyKey);
        Assert.Single(_cuentas.Cobrados);

        // Nada más que recuperar; y repetir el POST original devuelve el pago ya aprobado.
        Assert.Equal(0, await RecuperarAsync());
        var replay = await PagarAsync(100m);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal("APROBADO", (await Json(replay)).GetProperty("estado").GetString());
    }

    [Fact]
    public async Task Sin_respuesta_tras_el_maximo_de_intentos_pasa_a_revision_manual()
    {
        _cuentas.Modo = Falla.Caido;
        var pagoId = (await Json(await PagarAsync(100m))).GetProperty("pagoId").GetGuid(); // intento 1

        await RecuperarAsync(); // intento 2
        await RecuperarAsync(); // intento 3
        Assert.Equal("PENDIENTE", await EstadoAsync(pagoId));

        await RecuperarAsync(); // MaxIntentos = 3 alcanzado
        Assert.Equal("REVISION_MANUAL", await EstadoAsync(pagoId));
        Assert.Empty(_cuentas.Cobrados);
    }

    [Fact]
    public async Task Pago_ajeno_404_y_listado_solo_propio()
    {
        var id = (await Json(await PagarAsync(100m))).GetProperty("id").GetGuid();
        await PagarAsync(50m, key: "k-2", token: TestAuth.Token("user-2"));

        var ajeno = await _api.CreateClient().Como(TestAuth.Token("user-2")).GetAsync($"/api/pagos/{id}");
        Assert.Equal(HttpStatusCode.NotFound, ajeno.StatusCode);

        var mios = await _api.CreateClient().Como(TokenUser1).GetFromJsonAsync<JsonElement>("/api/pagos");
        Assert.Equal(1, mios.GetProperty("total").GetInt64());
    }

    [Fact]
    public async Task Admin_lista_todos_y_cliente_recibe_403()
    {
        await PagarAsync(100m);
        await PagarAsync(50m, key: "k-2", token: TestAuth.Token("user-2"));

        Assert.Equal(HttpStatusCode.Forbidden,
            (await _api.CreateClient().Como(TokenUser1).GetAsync("/api/admin/pagos")).StatusCode);

        var todos = await _api.CreateClient().Como(TestAuth.Token("admin", ["ADMIN"]))
            .GetFromJsonAsync<JsonElement>("/api/admin/pagos?estado=APROBADO");
        Assert.Equal(2, todos.GetProperty("total").GetInt64());
    }

    [Theory]
    [InlineData("/api/pagos", 0)]
    [InlineData("/api/pagos", 1_000_001)]
    // (pagina - 1) * tamano desbordaría int: antes llegaba un OFFSET negativo a Postgres y respondía 500.
    [InlineData("/api/pagos", int.MaxValue)]
    [InlineData("/api/admin/pagos", int.MaxValue)]
    public async Task Pagina_fuera_de_rango_400(string ruta, int pagina)
    {
        var resp = await _api.CreateClient().Como(TestAuth.Token("admin", ["ADMIN"]))
            .GetAsync($"{ruta}?pagina={pagina}&tamano=100");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Ultima_pagina_permitida_responde_vacia()
    {
        await PagarAsync(100m);
        var pagina = await _api.CreateClient().Como(TokenUser1)
            .GetFromJsonAsync<JsonElement>("/api/pagos?pagina=1000000&tamano=100");

        Assert.Equal(0, pagina.GetProperty("items").GetArrayLength());
        Assert.Equal(1, pagina.GetProperty("total").GetInt64());
    }

    [Fact]
    public async Task Sin_token_401()
    {
        var resp = await _api.CreateClient().PostAsJsonAsync("/api/pagos", new { cuentaId = Cuenta, monto = 1, metodo = "TARJETA" });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}
