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

    [HttpGet]
    public async Task<PaginaResponse<CuentaAdminResponse>> Listar(
        [FromQuery] int pagina = 1, [FromQuery] int tamano = 20, CancellationToken ct = default)
    {
        if (pagina < 1 || tamano < 1 || tamano > TamanoMaximo)
            throw new EntradaInvalidaException($"pagina debe ser ≥ 1 y tamano entre 1 y {TamanoMaximo}.");

        var (items, total) = await cuentas.ListarTodasAsync(pagina, tamano, ct);
        return new PaginaResponse<CuentaAdminResponse>(items.Select(CuentaAdminResponse.De).ToList(), pagina, tamano, total);
    }

    [HttpPost("{id:guid}/credito")]
    public async Task<CuentaAdminResponse> Acreditar(Guid id, CreditoRequest req, CancellationToken ct) =>
        CuentaAdminResponse.De(await cuentas.AcreditarAsync(id, req.Monto!.Value, ct));
}
