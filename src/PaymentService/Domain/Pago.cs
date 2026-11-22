using PaymentService.Domain.Metodos;

namespace PaymentService.Domain;

public enum EstadoPago
{
    PENDIENTE,
    APROBADO,
    RECHAZADO,
    /// <summary>Se agotaron los reintentos de débito: una persona debe revisarlo.</summary>
    REVISION_MANUAL,
}

/// <summary>
/// Orden de pago. Nace <see cref="EstadoPago.PENDIENTE"/> con la comisión ya calculada;
/// solo un pago pendiente puede cambiar de estado.
/// </summary>
public class Pago
{
    public const int MaxLongitudIdempotencyKey = 100;

    public Guid Id { get; private set; }
    public string OwnerId { get; private set; } = null!;
    public Guid CuentaId { get; private set; }
    public decimal Monto { get; private set; }
    public decimal Comision { get; private set; }
    public string Metodo { get; private set; } = null!;
    public EstadoPago Estado { get; private set; }
    public string IdempotencyKey { get; private set; } = null!;
    public string? MotivoRechazo { get; private set; }
    public int Intentos { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>xmin: evita que la recuperación y una petición cierren el mismo pago a la vez.</summary>
    public uint Version { get; private set; }

    /// <summary>Lo que se debita en AccountService.</summary>
    public decimal Total => Monto + Comision;

    private Pago() { }

    public Pago(string ownerId, Guid cuentaId, decimal monto, IMetodoPago metodo, string idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(ownerId))
            throw new ArgumentException("El dueño es obligatorio.", nameof(ownerId));
        if (cuentaId == Guid.Empty)
            throw new EntradaInvalidaException("cuentaId es obligatorio.");
        ValidarMonto(monto);
        ValidarIdempotencyKey(idempotencyKey);
        if (monto > metodo.Limite)
            throw new LimiteExcedidoException(metodo.Codigo, metodo.Limite);

        Id = Guid.NewGuid();
        OwnerId = ownerId;
        CuentaId = cuentaId;
        Monto = monto;
        Comision = metodo.CalcularComision(monto);
        Metodo = metodo.Codigo;
        Estado = EstadoPago.PENDIENTE;
        IdempotencyKey = idempotencyKey;
        CreatedAt = UpdatedAt = DateTimeOffset.UtcNow;
    }

    public static void ValidarMonto(decimal monto)
    {
        if (monto <= 0)
            throw new EntradaInvalidaException("El monto debe ser mayor que cero.");
        if (decimal.Round(monto, 2) != monto)
            throw new EntradaInvalidaException("El monto admite como máximo 2 decimales.");
    }

    public static void ValidarIdempotencyKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new EntradaInvalidaException("El header Idempotency-Key es obligatorio.");
        if (key.Length > MaxLongitudIdempotencyKey)
            throw new EntradaInvalidaException($"Idempotency-Key admite como máximo {MaxLongitudIdempotencyKey} caracteres.");
    }

    /// <summary>¿Es la misma petición que la original? Si no, la clave se reutilizó para otro pago.</summary>
    public bool MismaSolicitud(Guid cuentaId, decimal monto, string metodo) =>
        CuentaId == cuentaId && Monto == monto && string.Equals(Metodo, metodo?.Trim(), StringComparison.OrdinalIgnoreCase);

    public void Aprobar() => Cerrar(EstadoPago.APROBADO, null);

    public void Rechazar(string motivo) => Cerrar(EstadoPago.RECHAZADO, motivo);

    public void MarcarRevisionManual(string motivo) => Cerrar(EstadoPago.REVISION_MANUAL, motivo);

    public void RegistrarIntento()
    {
        AsegurarPendiente();
        Intentos++;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private void Cerrar(EstadoPago estado, string? motivo)
    {
        AsegurarPendiente();
        Estado = estado;
        MotivoRechazo = motivo;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private void AsegurarPendiente()
    {
        if (Estado != EstadoPago.PENDIENTE)
            throw new InvalidOperationException($"El pago {Id} ya está {Estado}.");
    }
}
