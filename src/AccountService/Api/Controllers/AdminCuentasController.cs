using AccountService.Api.Dtos;
using AccountService.Application;
using AccountService.Domain;
using AccountService.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AccountService.Api.Controllers;

[ApiController]
[Authorize(Policy = Politicas.Admin)]
[Route("api/admin/cuentas")]
public sealed class AdminCuentasController(CuentaService cuentas) : ControllerBase
{
    private const int TamanoMaximo = 100;
    // Tope para que (pagina - 1) * tamano no desborde int.
    private const int PaginaMaxima = 1_000_000;

    [HttpGet]
    public async Task<PaginaResponse<CuentaAdminResponse>> Listar(
        [FromQuery] int pagina = 1, [FromQuery] int tamano = 20, CancellationToken ct = default)
    {
        if (pagina < 1 || pagina > PaginaMaxima || tamano < 1 || tamano > TamanoMaximo)
            throw new EntradaInvalidaException($"pagina debe estar entre 1 y {PaginaMaxima} y tamano entre 1 y {TamanoMaximo}.");

        var (items, total) = await cuentas.ListarTodasAsync(pagina, tamano, ct);
        return new PaginaResponse<CuentaAdminResponse>(items.Select(CuentaAdminResponse.De).ToList(), pagina, tamano, total);
    }

    [HttpPost("{id:guid}/credito")]
    public async Task<ActionResult<MovimientoResponse>> Acreditar(
        Guid id, CreditoRequest req,
        [FromHeader(Name = IdempotencyHeaders.Key)] string? idempotencyKey,
        CancellationToken ct) =>
        this.Responder(await cuentas.AcreditarAsync(id, req.Monto!.Value, idempotencyKey, ct));
}
