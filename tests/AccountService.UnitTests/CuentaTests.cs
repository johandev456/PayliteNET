using AccountService.Domain;

namespace AccountService.UnitTests;

public class CuentaTests
{
    private static Cuenta NuevaConSaldo(decimal saldo)
    {
        var c = new Cuenta("user-1", "DOP");
        if (saldo > 0) c.Acreditar(saldo);
        return c;
    }

    [Fact]
    public void Nace_con_saldo_cero_y_fecha()
    {
        var c = new Cuenta("user-1", "DOP");

        Assert.Equal(0m, c.Saldo);
        Assert.NotEqual(Guid.Empty, c.Id);
        Assert.Equal("user-1", c.OwnerId);
        Assert.True(c.CreatedAt > DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    [Theory]
    [InlineData("dop")]
    [InlineData("DO")]
    [InlineData("DOPS")]
    [InlineData("")]
    [InlineData(null)]
    public void Moneda_invalida_es_400(string? moneda)
    {
        var ex = Assert.Throws<EntradaInvalidaException>(() => new Cuenta("user-1", moneda!));
        Assert.Equal(400, ex.Status);
    }

    [Fact]
    public void Sin_duenio_no_se_puede_crear()
    {
        Assert.Throws<ArgumentException>(() => new Cuenta(" ", "DOP"));
    }

    [Fact]
    public void Debitar_resta_el_monto()
    {
        var c = NuevaConSaldo(100m);
        c.Debitar(40.50m);
        Assert.Equal(59.50m, c.Saldo);
    }

    [Fact]
    public void Debitar_todo_el_saldo_deja_cero()
    {
        var c = NuevaConSaldo(100m);
        c.Debitar(100m);
        Assert.Equal(0m, c.Saldo);
    }

    [Fact]
    public void Saldo_insuficiente_es_422_y_no_cambia_el_saldo()
    {
        var c = NuevaConSaldo(10m);
        var ex = Assert.Throws<SaldoInsuficienteException>(() => c.Debitar(10.01m));
        Assert.Equal(422, ex.Status);
        Assert.Equal(10m, c.Saldo);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(0.001)]
    [InlineData(1.999)]
    public void Monto_invalido_es_400_en_debito_y_credito(double monto)
    {
        var c = NuevaConSaldo(100m);
        var m = (decimal)monto;
        Assert.Throws<EntradaInvalidaException>(() => c.Debitar(m));
        Assert.Throws<EntradaInvalidaException>(() => c.Acreditar(m));
        Assert.Equal(100m, c.Saldo);
    }

    [Fact]
    public void Credito_que_excede_el_maximo_se_rechaza()
    {
        var c = NuevaConSaldo(Montos.Maximo);
        Assert.Throws<EntradaInvalidaException>(() => c.Acreditar(0.01m));
    }
}
