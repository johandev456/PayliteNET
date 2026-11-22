using AccountService.Domain;
using AccountService.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AccountService.Application;

/// <param name="Cuenta">Estado actual de la cuenta (después de aplicar, o tal cual si es replay).</param>
/// <param name="Movimiento">El movimiento aplicado ahora, o el registrado la primera vez.</param>
/// <param name="Replay">true si la operación ya se había aplicado y no se volvió a tocar el saldo.</param>
public sealed record ResultadoMovimiento(Cuenta Cuenta, Movimiento Movimiento, bool Replay);

/// <summary>
/// Casos de uso de cuentas. Toda lectura de un cliente filtra por id <b>y</b> dueño:
/// una cuenta ajena es indistinguible de una inexistente (404).
/// </summary>
public sealed class CuentaService(AccountDbContext db, ILogger<CuentaService> log)
{
    internal const int MaxReintentosConcurrencia = 3;

    public async Task<Cuenta> CrearAsync(string ownerId, string moneda, CancellationToken ct)
    {
        var cuenta = new Cuenta(ownerId, moneda);
        db.Cuentas.Add(cuenta);
        await db.SaveChangesAsync(ct);
        return cuenta;
    }

    public async Task<Cuenta> ObtenerAsync(Guid id, string ownerId, CancellationToken ct) =>
        await db.Cuentas.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id && c.OwnerId == ownerId, ct)
        ?? throw new CuentaNoEncontradaException(id);

    public async Task<IReadOnlyList<Cuenta>> ListarAsync(string ownerId, CancellationToken ct) =>
        await db.Cuentas.AsNoTracking()
            .Where(c => c.OwnerId == ownerId)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(ct);

    public async Task<(IReadOnlyList<Cuenta> Items, long Total)> ListarTodasAsync(int pagina, int tamano, CancellationToken ct)
    {
        var query = db.Cuentas.AsNoTracking();
        var total = await query.LongCountAsync(ct);
        var items = await query
            .OrderBy(c => c.CreatedAt).ThenBy(c => c.Id)
            .Skip((pagina - 1) * tamano).Take(tamano)
            .ToListAsync(ct);
        return (items, total);
    }

    /// <summary>
    /// Débito idempotente. <paramref name="ownerId"/> sale del token (cliente) o del cuerpo validado
    /// contra la cuenta (llamada interna de servicio); en ambos casos se filtra por dueño.
    /// </summary>
    public Task<ResultadoMovimiento> DebitarAsync(Guid id, string ownerId, decimal monto, string operacionId, CancellationToken ct) =>
        AplicarAsync(id, ownerId, TipoMovimiento.Debito, monto, operacionId, ct);

    /// <summary>Solo ADMIN: sin filtro de dueño. Sin clave, cada llamada es una operación nueva.</summary>
    public Task<ResultadoMovimiento> AcreditarAsync(Guid id, decimal monto, string? operacionId, CancellationToken ct) =>
        AplicarAsync(id, null, TipoMovimiento.Credito, monto, operacionId ?? $"credito-{Guid.NewGuid()}", ct);

    /// <summary>
    /// 1. Si ya existe un movimiento con (cuenta, operación): replay, no se toca el saldo.
    /// 2. Si no, cambia el saldo e inserta el movimiento en el mismo SaveChanges (una transacción).
    /// 3. Dos peticiones simultáneas con la misma clave: una gana; la otra choca con el xmin de la
    ///    cuenta (reintento → ve el movimiento → replay) o con el UNIQUE (→ replay directo).
    /// </summary>
    private async Task<ResultadoMovimiento> AplicarAsync(
        Guid id, string? ownerId, TipoMovimiento tipo, decimal monto, string operacionId, CancellationToken ct)
    {
        Movimiento.ValidarOperacionId(operacionId);

        for (var intento = 1; ; intento++)
        {
            var cuenta = await db.Cuentas
                             .Where(c => c.Id == id && (ownerId == null || c.OwnerId == ownerId))
                             .FirstOrDefaultAsync(ct)
                         ?? throw new CuentaNoEncontradaException(id);

            var previo = await BuscarMovimientoAsync(id, operacionId, ct);
            if (previo is not null)
                return Replay(cuenta, previo, tipo, monto);

            if (tipo == TipoMovimiento.Debito) cuenta.Debitar(monto);
            else cuenta.Acreditar(monto);

            var movimiento = new Movimiento(id, tipo, monto, operacionId);
            db.Movimientos.Add(movimiento);

            try
            {
                await db.SaveChangesAsync(ct);
                return new ResultadoMovimiento(cuenta, movimiento, Replay: false);
            }
            catch (DbUpdateConcurrencyException) when (intento < MaxReintentosConcurrencia)
            {
                log.LogInformation("Conflicto de concurrencia en la cuenta {CuentaId}, reintento {Intento}", id, intento);
                db.ChangeTracker.Clear();
            }
            catch (DbUpdateConcurrencyException)
            {
                db.ChangeTracker.Clear();
                throw new ConcurrenciaException();
            }
            catch (DbUpdateException ex) when (EsDuplicadoDeOperacion(ex))
            {
                db.ChangeTracker.Clear();
                var ganador = await BuscarMovimientoAsync(id, operacionId, ct)
                              ?? throw new InvalidOperationException("Violación de unicidad sin movimiento previo.");
                var actual = await db.Cuentas.AsNoTracking().FirstAsync(c => c.Id == id, ct);
                return Replay(actual, ganador, tipo, monto);
            }
        }
    }

    private Task<Movimiento?> BuscarMovimientoAsync(Guid cuentaId, string operacionId, CancellationToken ct) =>
        db.Movimientos.AsNoTracking().FirstOrDefaultAsync(m => m.CuentaId == cuentaId && m.OperacionId == operacionId, ct);

    private static ResultadoMovimiento Replay(Cuenta cuenta, Movimiento previo, TipoMovimiento tipo, decimal monto) =>
        previo.Coincide(tipo, monto)
            ? new ResultadoMovimiento(cuenta, previo, Replay: true)
            : throw new OperacionConflictivaException(
                "Esta Idempotency-Key ya se usó para otra operación (otro tipo o monto) sobre la cuenta.");

    private static bool EsDuplicadoDeOperacion(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
        && pg.ConstraintName == AccountDbContext.UxMovimientoOperacion;
}
