namespace AccountService.Domain;

/// <summary>
/// Error de dominio con el código HTTP que le corresponde. El manejador global
/// lo convierte en un ProblemDetails; ningún controller decide códigos a mano.
/// </summary>
public abstract class DomainException(int status, string titulo, string detalle) : Exception(detalle)
{
    public int Status { get; } = status;
    public string Titulo { get; } = titulo;
}

/// <summary>Entrada inválida: monto ≤ 0, más de 2 decimales, moneda mal formada. 400.</summary>
public sealed class EntradaInvalidaException(string detalle)
    : DomainException(400, "Entrada inválida", detalle);

/// <summary>Regla de negocio: el saldo no alcanza. 422, no 400: la petición era válida.</summary>
public sealed class SaldoInsuficienteException(Guid cuentaId)
    : DomainException(422, "Saldo insuficiente", $"La cuenta {cuentaId} no tiene saldo suficiente.");

/// <summary>Cuenta inexistente o ajena: mismo 404 para no confirmar que existe.</summary>
public sealed class CuentaNoEncontradaException(Guid cuentaId)
    : DomainException(404, "Cuenta no encontrada", $"No existe la cuenta {cuentaId}.");

/// <summary>La misma Idempotency-Key se reutilizó con otro monto u otra operación.</summary>
public sealed class OperacionConflictivaException(string detalle)
    : DomainException(409, "Conflicto de operación", detalle);

/// <summary>Se agotaron los reintentos por conflicto de concurrencia (bloqueo optimista).</summary>
public sealed class ConcurrenciaException()
    : DomainException(409, "Conflicto de concurrencia", "La cuenta fue modificada por otra operación. Reintenta.");
