using AccountService.Api.Dtos;
using AccountService.Application;
using AccountService.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AccountService.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/cuentas")]
public sealed class CuentasController(CuentaService cuentas, ICurrentUser usuario) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<CuentaResponse>> Crear(CrearCuentaRequest req, CancellationToken ct)
    {
        var cuenta = await cuentas.CrearAsync(usuario.Id, req.Moneda!, ct);
        return CreatedAtAction(nameof(Obtener), new { id = cuenta.Id }, CuentaResponse.De(cuenta));
    }

    [HttpGet]
    public async Task<IEnumerable<CuentaResponse>> Listar(CancellationToken ct) =>
        (await cuentas.ListarAsync(usuario.Id, ct)).Select(CuentaResponse.De);

    [HttpGet("{id:guid}")]
    public async Task<CuentaResponse> Obtener(Guid id, CancellationToken ct) =>
        CuentaResponse.De(await cuentas.ObtenerAsync(id, usuario.Id, ct));

    [HttpPost("{id:guid}/debito")]
    public async Task<CuentaResponse> Debitar(Guid id, DebitoRequest req, CancellationToken ct) =>
        CuentaResponse.De(await cuentas.DebitarAsync(id, usuario.Id, req.Monto!.Value, ct));
}
