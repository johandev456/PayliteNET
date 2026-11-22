namespace PaymentService.Domain.Metodos;

/// <summary>
/// Strategy: cada método de pago trae su comisión y su límite. Añadir un método nuevo
/// es añadir una clase y registrarla en DI; nada más cambia (ni un switch que crezca).
/// </summary>
public interface IMetodoPago
{
    /// <summary>Código público del método (TARJETA, TRANSFERENCIA, BILLETERA).</summary>
    string Codigo { get; }

    /// <summary>Monto máximo por pago (sin contar la comisión).</summary>
    decimal Limite { get; }

    decimal CalcularComision(decimal monto);
}

public static class Comisiones
{
    /// <summary>
    /// Redondeo comercial a 2 decimales: 0.005 sube a 0.01 (<see cref="MidpointRounding.AwayFromZero"/>).
    /// Se elige a propósito: <c>Math.Round</c> de .NET usa por defecto redondeo bancario
    /// (ToEven: 0.125 → 0.12), que sorprende a quien revisa un recibo.
    /// </summary>
    public static decimal Redondear(decimal valor) => Math.Round(valor, 2, MidpointRounding.AwayFromZero);

    public static decimal Porcentaje(decimal monto, decimal porcentaje) => Redondear(monto * porcentaje / 100m);
}

public sealed class Tarjeta : IMetodoPago
{
    public string Codigo => "TARJETA";
    public decimal Limite => 50_000.00m;
    public decimal CalcularComision(decimal monto) => Comisiones.Porcentaje(monto, 2.9m);
}

public sealed class Transferencia : IMetodoPago
{
    public string Codigo => "TRANSFERENCIA";
    public decimal Limite => 500_000.00m;
    public decimal CalcularComision(decimal monto) => 25.00m;
}

public sealed class Billetera : IMetodoPago
{
    public string Codigo => "BILLETERA";
    public decimal Limite => 20_000.00m;
    public decimal CalcularComision(decimal monto) => Comisiones.Porcentaje(monto, 1m);
}
