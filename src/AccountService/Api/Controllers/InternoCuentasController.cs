using AccountService.Api.Dtos;
using AccountService.Application;
using AccountService.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AccountService.Api.Controllers;

/// <summary>
/// Solo para otros servicios (rol SERVICIO, token de client credentials). Lo usa la recuperación
/// de pagos PENDIENTE de PaymentService, cuando el token del usuario ya venció.
/// Sigue filtrando por dueño: un servicio no puede debitar una cuenta que no es del dueño del pago.
/// </summary>
[ApiController]
[Authorize(Policy = Politicas.Servicio)]
[Route("api/internal/cuentas")]
public sealed class InternoCuentasController(CuentaService cuentas) : ControllerBase
{
    [HttpPost("{id:guid}/debito")]
    public async Task<ActionResult<MovimientoResponse>> Debitar(
        Guid id, DebitoInternoRequest req,
        [FromHeader(Name = IdempotencyHeaders.Key)] string? idempotencyKey,
        CancellationToken ct) =>
        this.Responder(await cuentas.DebitarAsync(id, req.OwnerId!, req.Monto!.Value, idempotencyKey!, ct));
}
