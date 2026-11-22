using System.ComponentModel.DataAnnotations;
using PaymentService.Domain;

namespace PaymentService.Api.Dtos;

public sealed record CrearPagoRequest(
    [Required] Guid? CuentaId,
    [Required] decimal? Monto,
    [Required, StringLength(20, MinimumLength = 1)] string? Metodo);

/// <summary>Sin <c>ownerId</c> ni <c>idempotencyKey</c>: datos internos.</summary>
public sealed record PagoResponse(
    Guid Id,
    Guid CuentaId,
    decimal Monto,
    decimal Comision,
    decimal Total,
    string Metodo,
    EstadoPago Estado,
    string? MotivoRechazo,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static PagoResponse De(Pago p) =>
        new(p.Id, p.CuentaId, p.Monto, p.Comision, p.Total, p.Metodo, p.Estado, p.MotivoRechazo, p.CreatedAt, p.UpdatedAt);
}

public sealed record PagoAdminResponse(
    Guid Id,
    string OwnerId,
    Guid CuentaId,
    decimal Monto,
    decimal Comision,
    string Metodo,
    EstadoPago Estado,
    string? MotivoRechazo,
    int Intentos,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static PagoAdminResponse De(Pago p) =>
        new(p.Id, p.OwnerId, p.CuentaId, p.Monto, p.Comision, p.Metodo, p.Estado, p.MotivoRechazo, p.Intentos, p.CreatedAt, p.UpdatedAt);
}

public sealed record PaginaResponse<T>(IReadOnlyList<T> Items, int Pagina, int Tamano, long Total);

public static class Paginacion
{
    public const int TamanoMaximo = 100;

    public static void Validar(int pagina, int tamano)
    {
        if (pagina < 1 || tamano < 1 || tamano > TamanoMaximo)
            throw new EntradaInvalidaException($"pagina debe ser ≥ 1 y tamano entre 1 y {TamanoMaximo}.");
    }
}
