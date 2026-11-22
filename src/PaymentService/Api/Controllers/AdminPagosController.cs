using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PaymentService.Api.Dtos;
using PaymentService.Application;
using PaymentService.Domain;
using PaymentService.Security;

namespace PaymentService.Api.Controllers;

[ApiController]
[Authorize(Policy = Politicas.Admin)]
[Route("api/admin/pagos")]
public sealed class AdminPagosController(PagoService pagos) : ControllerBase
{
    /// <summary>Todos los pagos; filtro opcional por estado (útil para ver REVISION_MANUAL).</summary>
    [HttpGet]
    public async Task<PaginaResponse<PagoAdminResponse>> Listar(
        [FromQuery] EstadoPago? estado, [FromQuery] int pagina = 1, [FromQuery] int tamano = 20, CancellationToken ct = default)
    {
        Paginacion.Validar(pagina, tamano);
        var (items, total) = await pagos.ListarTodosAsync(estado, pagina, tamano, ct);
        return new PaginaResponse<PagoAdminResponse>(items.Select(PagoAdminResponse.De).ToList(), pagina, tamano, total);
    }
}
