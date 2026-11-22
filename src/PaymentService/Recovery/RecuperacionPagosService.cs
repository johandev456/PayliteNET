using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PaymentService.Application;
using PaymentService.Clients;
using PaymentService.Domain;
using PaymentService.Infrastructure;

namespace PaymentService.Recovery;

public sealed class RecuperacionOptions
{
    public bool Enabled { get; set; } = true;
    /// <summary>Cada cuánto se buscan pagos PENDIENTE.</summary>
    public TimeSpan Intervalo { get; set; } = TimeSpan.FromSeconds(10);
    /// <summary>Antigüedad mínima desde el último intento: no pisar un pago que una petición está procesando.</summary>
    public TimeSpan EsperaMinima { get; set; } = TimeSpan.FromSeconds(15);
    /// <summary>Intentos totales (incluido el de la petición original) antes de REVISION_MANUAL.</summary>
    public int MaxIntentos { get; set; } = 5;
    public int Lote { get; set; } = 50;
}

/// <summary>
/// Al arrancar y luego periódicamente, retoma los pagos PENDIENTE y reenvía el débito con el mismo
/// id de operación. Es seguro: si AccountService ya lo aplicó, responde replay y no cobra de nuevo.
/// Usa un token de servicio porque el del usuario ya venció.
/// </summary>
public sealed class RecuperacionPagosService(
    IServiceScopeFactory scopes,
    IOptions<RecuperacionOptions> opciones,
    TimeProvider reloj,
    ILogger<RecuperacionPagosService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        var o = opciones.Value;
        if (!o.Enabled)
        {
            log.LogInformation("Recuperación de pagos desactivada");
            return;
        }

        using var timer = new PeriodicTimer(o.Intervalo, reloj);
        do
        {
            try
            {
                var n = await RecuperarAsync(stop);
                if (n > 0) log.LogInformation("Recuperación: {N} pagos PENDIENTE procesados", n);
            }
            catch (Exception ex) when (!stop.IsCancellationRequested)
            {
                log.LogError(ex, "Fallo en la recuperación de pagos; se reintenta en el próximo ciclo");
            }
        } while (await timer.WaitForNextTickAsync(stop));
    }

    /// <summary>Una pasada. Público para los tests de integración.</summary>
    public async Task<int> RecuperarAsync(CancellationToken ct)
    {
        var o = opciones.Value;
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        var limite = reloj.GetUtcNow() - o.EsperaMinima;

        var ids = await db.Pagos.AsNoTracking()
            .Where(p => p.Estado == EstadoPago.PENDIENTE && p.UpdatedAt < limite)
            .OrderBy(p => p.UpdatedAt) // misma columna que el índice parcial: rango y orden salen del índice
            .Select(p => p.Id)
            .Take(o.Lote)
            .ToListAsync(ct);

        foreach (var id in ids)
        {
            // Un scope por pago: un fallo o conflicto en uno no contamina a los demás.
            using var s = scopes.CreateScope();
            await RecuperarUnoAsync(s.ServiceProvider, id, o, ct);
        }
        return ids.Count;
    }

    private async Task RecuperarUnoAsync(IServiceProvider sp, Guid id, RecuperacionOptions o, CancellationToken ct)
    {
        var db = sp.GetRequiredService<PaymentDbContext>();
        var pago = await db.Pagos.FirstOrDefaultAsync(p => p.Id == id && p.Estado == EstadoPago.PENDIENTE, ct);
        if (pago is null) return;

        try
        {
            if (pago.Intentos >= o.MaxIntentos)
            {
                pago.MarcarRevisionManual($"Sin respuesta de AccountService tras {pago.Intentos} intentos.");
                await db.SaveChangesAsync(ct);
                log.LogWarning("Pago {PagoId} pasa a REVISION_MANUAL", pago.Id);
                return;
            }

            var tokens = sp.GetRequiredService<IServiceTokenProvider>();
            var token = await tokens.ObtenerAsync(ct);
            var cuentas = sp.GetRequiredService<AccountClient>();
            await sp.GetRequiredService<PagoService>().ProcesarDebitoAsync(pago, async c =>
            {
                try
                {
                    return await cuentas.DebitarComoServicioAsync(pago, token, c);
                }
                catch (CuentasNoDisponibleException ex) when (ex.TokenRechazado)
                {
                    // El próximo intento pide un token nuevo en vez de reusar el rechazado hasta que venza.
                    tokens.Invalidar(token);
                    throw;
                }
            }, ct);
        }
        catch (ServicioCuentasException ex)
        {
            log.LogInformation("Pago {PagoId} sigue PENDIENTE (intento {Intento}): {Motivo}", pago.Id, pago.Intentos, ex.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            log.LogInformation("Pago {PagoId} lo cerró otro proceso", pago.Id);
        }
        catch (HttpRequestException ex)
        {
            // Keycloak caído: no se cuenta como intento contra AccountService.
            log.LogWarning(ex, "No se pudo obtener el token de servicio; el pago {PagoId} espera", pago.Id);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Timeout de Keycloak, JSON inválido, fallo de base...: no bloquear al resto del lote.
            log.LogError(ex, "Fallo inesperado recuperando el pago {PagoId}; se reintenta en el próximo ciclo", pago.Id);
        }
    }
}
