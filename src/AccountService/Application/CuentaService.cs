using AccountService.Domain;
using AccountService.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AccountService.Application;

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

    public Task<Cuenta> DebitarAsync(Guid id, string ownerId, decimal monto, CancellationToken ct) =>
        ConReintentoAsync(async () =>
        {
            var cuenta = await db.Cuentas.FirstOrDefaultAsync(c => c.Id == id && c.OwnerId == ownerId, ct)
                         ?? throw new CuentaNoEncontradaException(id);
            cuenta.Debitar(monto);
            await db.SaveChangesAsync(ct);
            return cuenta;
        });

    /// <summary>Solo ADMIN: sin filtro de dueño.</summary>
    public Task<Cuenta> AcreditarAsync(Guid id, decimal monto, CancellationToken ct) =>
        ConReintentoAsync(async () =>
        {
            var cuenta = await db.Cuentas.FirstOrDefaultAsync(c => c.Id == id, ct)
                         ?? throw new CuentaNoEncontradaException(id);
            cuenta.Acreditar(monto);
            await db.SaveChangesAsync(ct);
            return cuenta;
        });

    /// <summary>
    /// Bloqueo optimista con xmin: si otro proceso modificó la fila entre la lectura y el UPDATE,
    /// EF lanza DbUpdateConcurrencyException. Se relee y se reintenta; agotados los intentos → 409.
    /// </summary>
    private async Task<T> ConReintentoAsync<T>(Func<Task<T>> operacion)
    {
        for (var intento = 1; ; intento++)
        {
            try
            {
                return await operacion();
            }
            catch (DbUpdateConcurrencyException) when (intento < MaxReintentosConcurrencia)
            {
                log.LogInformation("Conflicto de concurrencia, reintento {Intento}", intento);
                db.ChangeTracker.Clear();
            }
            catch (DbUpdateConcurrencyException)
            {
                db.ChangeTracker.Clear();
                throw new ConcurrenciaException();
            }
        }
    }
}
