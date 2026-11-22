namespace PaymentService.Domain;

/// <summary>
/// Error con su código HTTP. El manejador global lo convierte en ProblemDetails;
/// <see cref="Extras"/> se añade como propiedades extra (por ejemplo <c>pagoId</c>).
/// </summary>
public abstract class DomainException(int status, string titulo, string detalle) : Exception(detalle)
{
    public int Status { get; } = status;
    public string Titulo { get; } = titulo;
    public virtual IReadOnlyDictionary<string, object?> Extras => new Dictionary<string, object?>();
}

public sealed class EntradaInvalidaException(string detalle)
    : DomainException(400, "Entrada inválida", detalle);

public sealed class MetodoPagoNoSoportadoException(string? metodo)
    : DomainException(400, "Método de pago no soportado", $"El método '{metodo}' no existe. Usa TARJETA, TRANSFERENCIA o BILLETERA.");

/// <summary>La petición es válida pero supera el límite del método: regla de negocio → 422.</summary>
public sealed class LimiteExcedidoException(string metodo, decimal limite)
    : DomainException(422, "Límite excedido", $"El método {metodo} admite como máximo {limite:0.00} por pago.");

public sealed class PagoNoEncontradoException(Guid id)
    : DomainException(404, "Pago no encontrado", $"No existe el pago {id}.");

/// <summary>Misma Idempotency-Key con otros datos: no es un reintento, es un error del cliente.</summary>
public sealed class IdempotencyKeyReutilizadaException()
    : DomainException(409, "Idempotency-Key reutilizada",
        "Ya existe un pago con esta Idempotency-Key y otros datos. Usa una clave nueva para un pago distinto.");

/// <summary>
/// AccountService no respondió bien. El pago queda PENDIENTE y lo retoma la recuperación;
/// se devuelve su id para que el cliente pueda consultarlo.
/// </summary>
public sealed class ServicioCuentasException(int status, string detalle, Guid pagoId)
    : DomainException(status, "Servicio de cuentas no disponible", detalle)
{
    public Guid PagoId { get; } = pagoId;

    public override IReadOnlyDictionary<string, object?> Extras =>
        new Dictionary<string, object?> { ["pagoId"] = PagoId, ["estado"] = nameof(EstadoPago.PENDIENTE) };
}
