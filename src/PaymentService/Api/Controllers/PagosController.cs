using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PaymentService.Api.Dtos;
using PaymentService.Application;
using PaymentService.Security;

namespace PaymentService.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/pagos")]
public sealed class PagosController(PagoService pagos, ICurrentUser usuario) : ControllerBase
{
    /// <summary>
    /// 201 con el pago nuevo (APROBADO o RECHAZADO: el pago se creó, el rechazo es su resultado).
    /// 200 + <c>Idempotent-Replayed: true</c> si la Idempotency-Key ya se había usado: mismo pago, sin cobrar.
    /// 502/503/504 si AccountService falla: el pago queda PENDIENTE y su id va en el ProblemDetails.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<PagoResponse>> Pagar(
        CrearPagoRequest req,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        var token = await HttpContext.GetTokenAsync("access_token")
                    ?? throw new InvalidOperationException("Token del usuario no disponible (SaveToken desactivado).");

        var r = await pagos.PagarAsync(
            usuario.Id, new SolicitudPago(req.CuentaId!.Value, req.Monto!.Value, req.Metodo!), idempotencyKey, token, ct);

        var body = PagoResponse.De(r.Pago);
        if (!r.Replay)
            return CreatedAtAction(nameof(Obtener), new { id = r.Pago.Id }, body);

        Response.Headers["Idempotent-Replayed"] = "true";
        return Ok(body);
    }

    [HttpGet("{id:guid}")]
    public async Task<PagoResponse> Obtener(Guid id, CancellationToken ct) =>
        PagoResponse.De(await pagos.ObtenerAsync(id, usuario.Id, ct));

    [HttpGet]
    public async Task<PaginaResponse<PagoResponse>> Listar(
        [FromQuery] int pagina = 1, [FromQuery] int tamano = 20, CancellationToken ct = default)
    {
        Paginacion.Validar(pagina, tamano);
        var (items, total) = await pagos.ListarAsync(usuario.Id, pagina, tamano, ct);
        return new PaginaResponse<PagoResponse>(items.Select(PagoResponse.De).ToList(), pagina, tamano, total);
    }
}
