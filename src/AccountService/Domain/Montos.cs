using System.Text.RegularExpressions;

namespace AccountService.Domain;

/// <summary>Validaciones de valores monetarios compartidas por la entidad.</summary>
public static partial class Montos
{
    /// <summary>NUMERIC(19,2): 17 dígitos enteros como máximo.</summary>
    public const decimal Maximo = 99_999_999_999_999_999.99m;

    public static void ValidarMonto(decimal monto)
    {
        if (monto <= 0)
            throw new EntradaInvalidaException("El monto debe ser mayor que cero.");
        if (decimal.Round(monto, 2) != monto)
            throw new EntradaInvalidaException("El monto admite como máximo 2 decimales.");
        if (monto > Maximo)
            throw new EntradaInvalidaException("El monto excede el máximo permitido.");
    }

    public static string ValidarMoneda(string? moneda)
    {
        if (moneda is null || !MonedaRegex().IsMatch(moneda))
            throw new EntradaInvalidaException("La moneda debe ser un código ISO 4217 de 3 letras mayúsculas (DOP, USD).");
        return moneda;
    }

    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex MonedaRegex();
}
