using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PayLite.Testing;

namespace AccountService.IntegrationTests;

[Collection(PostgresCollection.Nombre)]
public sealed class CuentasApiTests(PostgresFixture pg) : IAsyncLifetime
{
    private AccountApi _api = null!;

    private static readonly string Admin = TestAuth.Token("admin-1", ["ADMIN"]);
    private static readonly string Servicio = TestAuth.Token("svc-payment", ["SERVICIO"]);

    public Task InitializeAsync()
    {
        _api = new AccountApi(pg.NuevaBase("acc"));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    private HttpClient Cliente(string sub = "user-1") => _api.CreateClient().Como(TestAuth.Token(sub));

    private async Task<Guid> CrearCuentaAsync(string sub = "user-1", decimal saldo = 0m)
    {
        var resp = await Cliente(sub).PostAsJsonAsync("/api/cuentas", new { moneda = "DOP" });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var id = (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        if (saldo > 0)
        {
            var cred = await _api.CreateClient().Como(Admin)
                .PostAsJsonAsync($"/api/admin/cuentas/{id}/credito", new { monto = saldo });
            Assert.Equal(HttpStatusCode.OK, cred.StatusCode);
        }
        return id;
    }

    private async Task<decimal> SaldoAsync(Guid id, string sub = "user-1") =>
        (await Cliente(sub).GetFromJsonAsync<JsonElement>($"/api/cuentas/{id}")).GetProperty("saldo").GetDecimal();

    private Task<HttpResponseMessage> DebitarAsync(Guid id, decimal monto, string? key, string sub = "user-1")
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/cuentas/{id}/debito")
        {
            Content = JsonContent.Create(new { monto }),
        };
        if (key is not null) req.Headers.Add("Idempotency-Key", key);
        return Cliente(sub).SendAsync(req);
    }

    // ---------- Seguridad ----------

    [Fact]
    public async Task Sin_token_401_como_problem_details()
    {
        var resp = await _api.CreateClient().GetAsync("/api/cuentas");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        Assert.Equal("application/problem+json", resp.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("vencido")]
    [InlineData("firma")]
    [InlineData("audiencia")]
    [InlineData("emisor")]
    public async Task Token_invalido_401(string caso)
    {
        var token = caso switch
        {
            "vencido" => TestAuth.Token("user-1", vence: TimeSpan.FromMinutes(-5)),
            "firma" => TestAuth.Token("user-1", firmaAjena: true),
            "audiencia" => TestAuth.Token("user-1", audience: "otra-api"),
            _ => TestAuth.Token("user-1", issuer: "http://otro/realms/x"),
        };
        var resp = await _api.CreateClient().Como(token).GetAsync("/api/cuentas");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Token_sin_sub_401_y_no_500()
    {
        var resp = await _api.CreateClient().Como(TestAuth.Token(sub: null))
            .PostAsJsonAsync("/api/cuentas", new { moneda = "DOP" });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Cliente_en_admin_403_y_admin_200()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await Cliente().GetAsync("/api/admin/cuentas")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _api.CreateClient().Como(Admin).GetAsync("/api/admin/cuentas")).StatusCode);
    }

    [Fact]
    public async Task Cuenta_ajena_404_igual_que_inexistente()
    {
        var id = await CrearCuentaAsync("user-1");

        Assert.Equal(HttpStatusCode.NotFound, (await Cliente("user-2").GetAsync($"/api/cuentas/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Cliente("user-1").GetAsync($"/api/cuentas/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await DebitarAsync(id, 1m, "k", "user-2")).StatusCode);

        // Un ADMIN tampoco ve cuentas ajenas por la ruta normal: el rol abre /api/admin, no quita el filtro.
        Assert.Equal(HttpStatusCode.NotFound, (await _api.CreateClient().Como(Admin).GetAsync($"/api/cuentas/{id}")).StatusCode);
    }

    [Fact]
    public async Task Listar_solo_devuelve_las_propias_sin_owner()
    {
        await CrearCuentaAsync("user-1");
        await CrearCuentaAsync("user-2");

        var cuentas = await Cliente("user-1").GetFromJsonAsync<JsonElement>("/api/cuentas");
        Assert.Equal(1, cuentas.GetArrayLength());
        Assert.False(cuentas[0].TryGetProperty("ownerId", out _));
    }

    // ---------- Reglas ----------

    [Fact]
    public async Task Cuenta_nueva_tiene_saldo_cero_y_el_primer_debito_es_422()
    {
        var id = await CrearCuentaAsync();
        Assert.Equal(0m, await SaldoAsync(id));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await DebitarAsync(id, 1m, "k1")).StatusCode);
    }

    [Theory]
    [InlineData("""{"moneda":"dop"}""")]
    [InlineData("""{"moneda":"DOLAR"}""")]
    [InlineData("""{}""")]
    [InlineData("""no es json""")]
    public async Task Crear_con_entrada_invalida_400(string cuerpo)
    {
        var resp = await Cliente().PostAsync("/api/cuentas", new StringContent(cuerpo, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(1.005)]
    public async Task Debito_con_monto_invalido_400(double monto)
    {
        var id = await CrearCuentaAsync(saldo: 100m);
        Assert.Equal(HttpStatusCode.BadRequest, (await DebitarAsync(id, (decimal)monto, "k")).StatusCode);
        Assert.Equal(100m, await SaldoAsync(id));
    }

    // ---------- Idempotencia ----------

    [Fact]
    public async Task Debito_sin_idempotency_key_400()
    {
        var id = await CrearCuentaAsync(saldo: 100m);
        Assert.Equal(HttpStatusCode.BadRequest, (await DebitarAsync(id, 10m, null)).StatusCode);
    }

    [Fact]
    public async Task Debito_repetido_con_la_misma_clave_no_cobra_dos_veces()
    {
        var id = await CrearCuentaAsync(saldo: 100m);

        var r1 = await DebitarAsync(id, 30m, "op-1");
        var r2 = await DebitarAsync(id, 30m, "op-1");

        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        Assert.False(r1.Headers.Contains("Idempotent-Replayed"));
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
        Assert.Equal("true", r2.Headers.GetValues("Idempotent-Replayed").Single());
        Assert.Equal(
            (await r1.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("movimientoId").GetGuid(),
            (await r2.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("movimientoId").GetGuid());
        Assert.Equal(70m, await SaldoAsync(id));
    }

    [Fact]
    public async Task Misma_clave_con_otro_monto_409()
    {
        var id = await CrearCuentaAsync(saldo: 100m);
        await DebitarAsync(id, 30m, "op-1");

        Assert.Equal(HttpStatusCode.Conflict, (await DebitarAsync(id, 31m, "op-1")).StatusCode);
        Assert.Equal(70m, await SaldoAsync(id));
    }

    [Fact]
    public async Task Debito_rechazado_por_saldo_se_puede_reintentar_con_la_misma_clave()
    {
        var id = await CrearCuentaAsync(saldo: 10m);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await DebitarAsync(id, 50m, "op-1")).StatusCode);

        await _api.CreateClient().Como(Admin).PostAsJsonAsync($"/api/admin/cuentas/{id}/credito", new { monto = 100m });
        Assert.Equal(HttpStatusCode.OK, (await DebitarAsync(id, 50m, "op-1")).StatusCode);
        Assert.Equal(60m, await SaldoAsync(id));
    }

    [Fact]
    public async Task Debitos_simultaneos_con_la_misma_clave_cobran_una_vez()
    {
        var id = await CrearCuentaAsync(saldo: 100m);

        var respuestas = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => DebitarAsync(id, 10m, "misma-op")));

        Assert.All(respuestas, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(90m, await SaldoAsync(id));
    }

    [Fact]
    public async Task Debitos_simultaneos_distintos_nunca_dejan_saldo_negativo()
    {
        var id = await CrearCuentaAsync(saldo: 100m);

        var respuestas = await Task.WhenAll(Enumerable.Range(0, 15).Select(i => DebitarAsync(id, 10m, $"op-{i}")));
        var aprobados = respuestas.Count(r => r.StatusCode == HttpStatusCode.OK);

        // Bajo contención algunos pueden agotar reintentos (409), pero nunca hay lost update.
        Assert.All(respuestas, r => Assert.Contains(r.StatusCode,
            new[] { HttpStatusCode.OK, HttpStatusCode.UnprocessableEntity, HttpStatusCode.Conflict }));
        Assert.InRange(aprobados, 1, 10);
        Assert.Equal(100m - 10m * aprobados, await SaldoAsync(id));
    }

    // ---------- Endpoint interno ----------

    private Task<HttpResponseMessage> DebitoInternoAsync(string token, Guid id, string ownerId, decimal monto, string key)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/internal/cuentas/{id}/debito")
        {
            Content = JsonContent.Create(new { monto, ownerId }),
        };
        req.Headers.Add("Idempotency-Key", key);
        return _api.CreateClient().Como(token).SendAsync(req);
    }

    [Fact]
    public async Task Interno_exige_rol_servicio()
    {
        var id = await CrearCuentaAsync(saldo: 100m);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await DebitoInternoAsync(TestAuth.Token("user-1"), id, "user-1", 10m, "k")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await DebitoInternoAsync(Admin, id, "user-1", 10m, "k")).StatusCode);
    }

    [Fact]
    public async Task Interno_valida_el_duenio_y_comparte_idempotencia_con_el_endpoint_publico()
    {
        var id = await CrearCuentaAsync("user-1", saldo: 100m);

        Assert.Equal(HttpStatusCode.NotFound, (await DebitoInternoAsync(Servicio, id, "user-2", 10m, "pago-1")).StatusCode);

        // Primero el usuario debita con la clave del pago; la recuperación reintenta con la misma: replay.
        Assert.Equal(HttpStatusCode.OK, (await DebitarAsync(id, 10m, "pago-1")).StatusCode);
        var replay = await DebitoInternoAsync(Servicio, id, "user-1", 10m, "pago-1");
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.True(replay.Headers.Contains("Idempotent-Replayed"));
        Assert.Equal(90m, await SaldoAsync(id));
    }

    // ---------- Admin ----------

    [Fact]
    public async Task Admin_lista_paginado_con_owner()
    {
        await CrearCuentaAsync("user-1");
        await CrearCuentaAsync("user-2");
        await CrearCuentaAsync("user-3");

        var pagina = await _api.CreateClient().Como(Admin).GetFromJsonAsync<JsonElement>("/api/admin/cuentas?pagina=1&tamano=2");
        Assert.Equal(3, pagina.GetProperty("total").GetInt64());
        Assert.Equal(2, pagina.GetProperty("items").GetArrayLength());
        Assert.True(pagina.GetProperty("items")[0].TryGetProperty("ownerId", out _));

        Assert.Equal(HttpStatusCode.BadRequest,
            (await _api.CreateClient().Como(Admin).GetAsync("/api/admin/cuentas?tamano=1000")).StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1_000_001)]
    // (pagina - 1) * tamano desbordaría int: antes llegaba un OFFSET negativo a Postgres y respondía 500.
    [InlineData(int.MaxValue)]
    public async Task Admin_pagina_fuera_de_rango_400(int pagina)
    {
        var resp = await _api.CreateClient().Como(Admin).GetAsync($"/api/admin/cuentas?pagina={pagina}&tamano=100");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Admin_ultima_pagina_permitida_responde_vacia()
    {
        await CrearCuentaAsync();
        var pagina = await _api.CreateClient().Como(Admin)
            .GetFromJsonAsync<JsonElement>("/api/admin/cuentas?pagina=1000000&tamano=100");

        Assert.Equal(0, pagina.GetProperty("items").GetArrayLength());
        Assert.Equal(1, pagina.GetProperty("total").GetInt64());
    }
}
