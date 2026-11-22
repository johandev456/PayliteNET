using PaymentService.Domain;
using PaymentService.Domain.Metodos;

namespace PaymentService.UnitTests;

public class PagoTests
{
    private static readonly Guid Cuenta = Guid.NewGuid();

    private static Pago Nuevo(decimal monto = 100m, IMetodoPago? metodo = null) =>
        new("user-1", Cuenta, monto, metodo ?? new Tarjeta(), "key-1");

    [Fact]
    public void Nace_pendiente_con_comision_calculada()
    {
        var p = Nuevo(100m);

        Assert.Equal(EstadoPago.PENDIENTE, p.Estado);
        Assert.Equal(2.90m, p.Comision);
        Assert.Equal(102.90m, p.Total);
        Assert.Equal("TARJETA", p.Metodo);
        Assert.Equal(0, p.Intentos);
    }

    [Fact]
    public void Monto_en_el_limite_se_acepta() =>
        Assert.Equal(EstadoPago.PENDIENTE, Nuevo(50_000.00m).Estado);

    [Fact]
    public void Superar_el_limite_es_422()
    {
        var ex = Assert.Throws<LimiteExcedidoException>(() => Nuevo(50_000.01m));
        Assert.Equal(422, ex.Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1.001)]
    public void Monto_invalido_es_400(double monto) =>
        Assert.Throws<EntradaInvalidaException>(() => Nuevo((decimal)monto));

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void Idempotency_key_obligatoria(string? key) =>
        Assert.Throws<EntradaInvalidaException>(() => new Pago("user-1", Cuenta, 10m, new Tarjeta(), key!));

    [Fact]
    public void Idempotency_key_demasiado_larga() =>
        Assert.Throws<EntradaInvalidaException>(() => new Pago("user-1", Cuenta, 10m, new Tarjeta(), new string('k', 101)));

    [Fact]
    public void Solo_un_pendiente_cambia_de_estado()
    {
        var p = Nuevo();
        p.Aprobar();

        Assert.Equal(EstadoPago.APROBADO, p.Estado);
        Assert.Throws<InvalidOperationException>(() => p.Rechazar("x"));
        Assert.Throws<InvalidOperationException>(() => p.RegistrarIntento());
    }

    [Fact]
    public void Rechazar_guarda_el_motivo()
    {
        var p = Nuevo();
        p.Rechazar("Saldo insuficiente");
        Assert.Equal(EstadoPago.RECHAZADO, p.Estado);
        Assert.Equal("Saldo insuficiente", p.MotivoRechazo);
    }

    [Fact]
    public void Misma_solicitud_compara_cuenta_monto_y_metodo()
    {
        var p = Nuevo(100m);
        Assert.True(p.MismaSolicitud(Cuenta, 100m, "tarjeta"));
        Assert.False(p.MismaSolicitud(Cuenta, 101m, "TARJETA"));
        Assert.False(p.MismaSolicitud(Guid.NewGuid(), 100m, "TARJETA"));
        Assert.False(p.MismaSolicitud(Cuenta, 100m, "BILLETERA"));
    }
}
