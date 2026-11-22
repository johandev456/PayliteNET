using System.ComponentModel.DataAnnotations;
using AccountService.Domain;

namespace AccountService.Api.Dtos;

// Las DataAnnotations solo validan forma (presente, longitud). Las reglas de negocio
// (monto > 0, 2 decimales, saldo suficiente) viven en la entidad.

public sealed record CrearCuentaRequest(
    [Required, StringLength(3, MinimumLength = 3)] string? Moneda);

public sealed record DebitoRequest(
    [Required] decimal? Monto);

public sealed record CreditoRequest(
    [Required] decimal? Monto);

/// <summary>Sin <c>ownerId</c>: el dueño no se expone hacia afuera.</summary>
public sealed record CuentaResponse(Guid Id, decimal Saldo, string Moneda, DateTimeOffset CreatedAt)
{
    public static CuentaResponse De(Cuenta c) => new(c.Id, c.Saldo, c.Moneda, c.CreatedAt);
}

/// <summary>Vista de administración: aquí sí se ve el dueño.</summary>
public sealed record CuentaAdminResponse(Guid Id, string OwnerId, decimal Saldo, string Moneda, DateTimeOffset CreatedAt)
{
    public static CuentaAdminResponse De(Cuenta c) => new(c.Id, c.OwnerId, c.Saldo, c.Moneda, c.CreatedAt);
}

public sealed record PaginaResponse<T>(IReadOnlyList<T> Items, int Pagina, int Tamano, long Total);
