using System.ComponentModel.DataAnnotations;
using AccountService.Application;

namespace AccountService.Api.Dtos;

/// <summary>
/// Cuerpo del débito interno (PaymentService con token de servicio). El <c>ownerId</c>
/// no da permisos: solo se usa para comprobar que la cuenta pertenece a quien hizo el pago.
/// </summary>
public sealed record DebitoInternoRequest(
    [Required] decimal? Monto,
    [Required, StringLength(255, MinimumLength = 1)] string? OwnerId);

/// <param name="Saldo">Saldo actual de la cuenta. En un replay es el de ahora, no el de la primera vez.</param>
public sealed record MovimientoResponse(
    Guid MovimientoId,
    Guid CuentaId,
    string Tipo,
    decimal Monto,
    string OperacionId,
    decimal Saldo,
    DateTimeOffset CreatedAt)
{
    public static MovimientoResponse De(ResultadoMovimiento r) => new(
        r.Movimiento.Id,
        r.Cuenta.Id,
        r.Movimiento.Tipo.ToString().ToUpperInvariant(),
        r.Movimiento.Monto,
        r.Movimiento.OperacionId,
        r.Cuenta.Saldo,
        r.Movimiento.CreatedAt);
}
