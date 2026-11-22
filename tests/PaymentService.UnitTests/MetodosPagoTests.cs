using PaymentService.Domain;
using PaymentService.Domain.Metodos;

namespace PaymentService.UnitTests;

public class MetodosPagoTests
{
    private static MetodoPagoFactory Factory() => new([new Tarjeta(), new Transferencia(), new Billetera()]);

    [Theory]
    [InlineData(100.00, 2.90)]
    [InlineData(1000.00, 29.00)]
    [InlineData(10.00, 0.29)]
    // 0.29 * 2.9% = 0.00841 → 0.01 (no 0.00)
    [InlineData(0.29, 0.01)]
    // 12.50 * 2.9% = 0.3625 → 0.36
    [InlineData(12.50, 0.36)]
    public void Tarjeta_cobra_2_9_por_ciento(double monto, double esperado) =>
        Assert.Equal((decimal)esperado, new Tarjeta().CalcularComision((decimal)monto));

    [Theory]
    [InlineData(1.00)]
    [InlineData(500_000.00)]
    public void Transferencia_cobra_fijo_25(double monto) =>
        Assert.Equal(25.00m, new Transferencia().CalcularComision((decimal)monto));

    [Theory]
    [InlineData(100.00, 1.00)]
    // 0.50 * 1% = 0.005: el punto medio sube (AwayFromZero). Con ToEven sería 0.00.
    [InlineData(0.50, 0.01)]
    // 2.50 * 1% = 0.025 → 0.03 (ToEven daría 0.02).
    [InlineData(2.50, 0.03)]
    public void Billetera_cobra_1_por_ciento_con_redondeo_comercial(double monto, double esperado) =>
        Assert.Equal((decimal)esperado, new Billetera().CalcularComision((decimal)monto));

    [Fact]
    public void Redondeo_es_comercial_no_bancario()
    {
        Assert.Equal(0.13m, Comisiones.Redondear(0.125m));
        Assert.Equal(0.12m, Math.Round(0.125m, 2)); // el default de .NET, que evitamos
    }

    [Theory]
    [InlineData("TARJETA", typeof(Tarjeta))]
    [InlineData("tarjeta", typeof(Tarjeta))]
    [InlineData(" Transferencia ", typeof(Transferencia))]
    [InlineData("BILLETERA", typeof(Billetera))]
    public void Factory_elige_la_estrategia_por_codigo(string codigo, Type tipo) =>
        Assert.IsType(tipo, Factory().Obtener(codigo));

    [Theory]
    [InlineData("CHEQUE")]
    [InlineData("")]
    [InlineData(null)]
    public void Factory_metodo_desconocido_es_400(string? codigo)
    {
        var ex = Assert.Throws<MetodoPagoNoSoportadoException>(() => Factory().Obtener(codigo));
        Assert.Equal(400, ex.Status);
    }

    [Fact]
    public void Factory_rechaza_codigos_duplicados_al_construirse() =>
        Assert.Throws<ArgumentException>(() => new MetodoPagoFactory([new Tarjeta(), new Tarjeta()]));

    [Theory]
    [InlineData(typeof(Tarjeta), 50_000.00)]
    [InlineData(typeof(Transferencia), 500_000.00)]
    [InlineData(typeof(Billetera), 20_000.00)]
    public void Limites_por_metodo(Type tipo, double limite) =>
        Assert.Equal((decimal)limite, ((IMetodoPago)Activator.CreateInstance(tipo)!).Limite);
}
