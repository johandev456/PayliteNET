namespace AccountService.Domain;

public enum TipoMovimiento
{
    Debito,
    Credito,
}

/// <summary>
/// Registro de cada débito o crédito. <c>UNIQUE (cuenta_id, operacion_id)</c> hace que
/// la misma operación no pueda aplicarse dos veces: es la base de la idempotencia.
/// </summary>
public class Movimiento
{
    public const int MaxLongitudOperacionId = 100;

    public Guid Id { get; private set; }
    public Guid CuentaId { get; private set; }
    public TipoMovimiento Tipo { get; private set; }
    public decimal Monto { get; private set; }
    public string OperacionId { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }

    private Movimiento() { }

    public Movimiento(Guid cuentaId, TipoMovimiento tipo, decimal monto, string operacionId)
    {
        Montos.ValidarMonto(monto);
        ValidarOperacionId(operacionId);

        Id = Guid.NewGuid();
        CuentaId = cuentaId;
        Tipo = tipo;
        Monto = monto;
        OperacionId = operacionId;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public static void ValidarOperacionId(string? operacionId)
    {
        if (string.IsNullOrWhiteSpace(operacionId))
            throw new EntradaInvalidaException("El header Idempotency-Key es obligatorio.");
        if (operacionId.Length > MaxLongitudOperacionId)
            throw new EntradaInvalidaException($"Idempotency-Key admite como máximo {MaxLongitudOperacionId} caracteres.");
    }

    /// <summary>¿Es la misma operación que se pide ahora? Si no, la clave se está reutilizando mal.</summary>
    public bool Coincide(TipoMovimiento tipo, decimal monto) => Tipo == tipo && Monto == monto;
}
