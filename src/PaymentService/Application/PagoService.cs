using Microsoft.EntityFrameworkCore;
using Npgsql;
using PaymentService.Clients;
using PaymentService.Domain;
using PaymentService.Domain.Metodos;
using PaymentService.Infrastructure;

namespace PaymentService.Application;

public sealed record SolicitudPago(Guid CuentaId, decimal Monto, string Metodo);

public sealed record ResultadoPago(Pago Pago, bool Replay);

public sealed class PagoService(
    PaymentDbContext db,
    MetodoPagoFactory metodos,
    AccountClient cuentas,
    ILogger<PagoService> log)
{
    /// <summary>
    /// Flujo de un pago:
    /// 1. ¿Ya hay un pago de este dueño con esta Idempotency-Key? → se devuelve tal cual (sin cobrar).
    /// 2. Estrategia por método, límite y comisión.
    /// 3. Se guarda PENDIENTE antes de llamar a AccountService (queda rastro aunque todo se caiga).
    /// 4. Débito con el token del usuario y el id del pago como clave de operación.
    /// 5. APROBADO / RECHAZADO; si AccountService no responde, queda PENDIENTE para la recuperación.
    /// </summary>
    public async Task<ResultadoPago> PagarAsync(
        string ownerId, SolicitudPago s, string? idempotencyKey, string tokenUsuario, CancellationToken ct)
    {
        Pago.ValidarIdempotencyKey(idempotencyKey);

        var previo = await BuscarPorClaveAsync(ownerId, idempotencyKey!, ct);
        if (previo is not null)
            return Replay(previo, s);

        var pago = new Pago(ownerId, s.CuentaId, s.Monto, metodos.Obtener(s.Metodo), idempotencyKey!);
        db.Pagos.Add(pago);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Dos peticiones simultáneas con la misma clave: ganó la otra.
            db.ChangeTracker.Clear();
            var ganador = await BuscarPorClaveAsync(ownerId, idempotencyKey!, ct)
                          ?? throw new InvalidOperationException("Violación de unicidad sin pago previo.");
            return Replay(ganador, s);
        }

        pago = await ProcesarDebitoAsync(pago, c => cuentas.DebitarComoUsuarioAsync(pago, tokenUsuario, c), ct);
        return new ResultadoPago(pago, Replay: false);
    }

    /// <summary>
    /// Un intento de débito sobre un pago PENDIENTE. Usado por la petición y por la recuperación.
    /// Si AccountService no responde, lanza <see cref="ServicioCuentasException"/> y el pago sigue PENDIENTE.
    /// </summary>
    public async Task<Pago> ProcesarDebitoAsync(Pago pago, Func<CancellationToken, Task<RespuestaDebito>> debitar, CancellationToken ct)
    {
        try
        {
            pago.RegistrarIntento();
            await db.SaveChangesAsync(ct);

            RespuestaDebito r;
            try
            {
                r = await debitar(ct);
            }
            catch (CuentasNoDisponibleException ex)
            {
                log.LogWarning(ex, "Pago {PagoId} queda PENDIENTE: {Motivo}", pago.Id, ex.Message);
                throw new ServicioCuentasException(ex.Status, ex.Message + " El pago quedó PENDIENTE y se reintentará.", pago.Id);
            }

            if (r.Resultado == ResultadoDebito.Aprobado) pago.Aprobar();
            else pago.Rechazar(r.Motivo ?? "Rechazado por AccountService");
            await db.SaveChangesAsync(ct);
            log.LogInformation("Pago {PagoId} {Estado}", pago.Id, pago.Estado);
            return pago;
        }
        catch (DbUpdateConcurrencyException)
        {
            // Otro proceso (petición o recuperación) cerró este pago mientras tanto. Manda lo que haya en la base.
            db.ChangeTracker.Clear();
            log.LogInformation("Pago {PagoId} modificado por otro proceso; se devuelve su estado actual", pago.Id);
            return await db.Pagos.AsNoTracking().FirstAsync(p => p.Id == pago.Id, ct);
        }
    }

    public async Task<Pago> ObtenerAsync(Guid id, string ownerId, CancellationToken ct) =>
        await db.Pagos.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id && p.OwnerId == ownerId, ct)
        ?? throw new PagoNoEncontradoException(id);

    public Task<(IReadOnlyList<Pago> Items, long Total)> ListarAsync(string ownerId, int pagina, int tamano, CancellationToken ct) =>
        PaginarAsync(db.Pagos.AsNoTracking().Where(p => p.OwnerId == ownerId), pagina, tamano, ct);

    public Task<(IReadOnlyList<Pago> Items, long Total)> ListarTodosAsync(EstadoPago? estado, int pagina, int tamano, CancellationToken ct) =>
        PaginarAsync(db.Pagos.AsNoTracking().Where(p => estado == null || p.Estado == estado), pagina, tamano, ct);

    private static async Task<(IReadOnlyList<Pago>, long)> PaginarAsync(IQueryable<Pago> q, int pagina, int tamano, CancellationToken ct)
    {
        var total = await q.LongCountAsync(ct);
        var items = await q.OrderByDescending(p => p.CreatedAt).ThenBy(p => p.Id)
            .Skip((pagina - 1) * tamano).Take(tamano).ToListAsync(ct);
        return (items, total);
    }

    private Task<Pago?> BuscarPorClaveAsync(string ownerId, string key, CancellationToken ct) =>
        db.Pagos.AsNoTracking().FirstOrDefaultAsync(p => p.OwnerId == ownerId && p.IdempotencyKey == key, ct);

    private static ResultadoPago Replay(Pago previo, SolicitudPago s) =>
        previo.MismaSolicitud(s.CuentaId, s.Monto, s.Metodo)
            ? new ResultadoPago(previo, Replay: true)
            : throw new IdempotencyKeyReutilizadaException();
}
