using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using PaymentService.Domain;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace PaymentService.Clients;

public enum ResultadoDebito
{
    Aprobado,
    Rechazado,
}

public sealed record RespuestaDebito(ResultadoDebito Resultado, string? Motivo = null);

/// <summary>
/// AccountService no respondió de forma útil (caído, timeout, 5xx). El débito pudo o no aplicarse:
/// el pago se queda PENDIENTE y se reintenta con el mismo id de operación.
/// </summary>
public sealed class CuentasNoDisponibleException(int status, string detalle, Exception? inner = null, bool tokenRechazado = false)
    : Exception(detalle, inner)
{
    /// <summary>AccountService respondió 401/403: el token enviado no sirve (vencido, revocado, sin rol).</summary>
    public bool TokenRechazado { get; } = tokenRechazado;

    /// <summary>502 respuesta inválida/5xx, 503 sin conexión o circuito abierto, 504 timeout.</summary>
    public int Status { get; } = status;
}

/// <summary>
/// Cliente HTTP tipado de AccountService. Lleva el resilience handler estándar (timeout por intento,
/// reintentos con backoff, circuit breaker). Reintentar un POST es seguro porque el débito es
/// idempotente: la Idempotency-Key es el id del pago.
/// </summary>
public sealed class AccountClient(HttpClient http, ILogger<AccountClient> log)
{
    /// <summary>Débito en nombre del usuario, reenviando su propio token.</summary>
    public Task<RespuestaDebito> DebitarComoUsuarioAsync(Pago pago, string tokenUsuario, CancellationToken ct) =>
        EnviarAsync($"api/cuentas/{pago.CuentaId}/debito", new { monto = pago.Total }, pago, tokenUsuario, ct);

    /// <summary>Débito de la recuperación, con token de servicio (rol SERVICIO) y el dueño del pago.</summary>
    public Task<RespuestaDebito> DebitarComoServicioAsync(Pago pago, string tokenServicio, CancellationToken ct) =>
        EnviarAsync($"api/internal/cuentas/{pago.CuentaId}/debito",
            new { monto = pago.Total, ownerId = pago.OwnerId }, pago, tokenServicio, ct);

    private async Task<RespuestaDebito> EnviarAsync(string ruta, object cuerpo, Pago pago, string token, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, ruta) { Content = JsonContent.Create(cuerpo) };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.Add("Idempotency-Key", pago.Id.ToString());

        HttpResponseMessage resp;
        try
        {
            resp = await http.SendAsync(req, ct);
        }
        catch (TimeoutRejectedException ex)
        {
            throw new CuentasNoDisponibleException(504, "AccountService no respondió a tiempo.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new CuentasNoDisponibleException(504, "AccountService no respondió a tiempo.", ex);
        }
        catch (BrokenCircuitException ex)
        {
            throw new CuentasNoDisponibleException(503, "AccountService no está disponible (circuito abierto).", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new CuentasNoDisponibleException(503, "No se pudo conectar con AccountService.", ex);
        }

        using (resp)
        {
            if (resp.IsSuccessStatusCode)
                return new RespuestaDebito(ResultadoDebito.Aprobado);

            var problema = await LeerProblemaAsync(resp, ct);
            switch (resp.StatusCode)
            {
                // Decisiones de negocio definitivas: el débito no se hizo ni se hará.
                case HttpStatusCode.UnprocessableEntity:
                    return new RespuestaDebito(ResultadoDebito.Rechazado, problema ?? "Saldo insuficiente");
                case HttpStatusCode.NotFound:
                    return new RespuestaDebito(ResultadoDebito.Rechazado, "Cuenta no encontrada");
                case HttpStatusCode.BadRequest:
                    return new RespuestaDebito(ResultadoDebito.Rechazado, problema ?? "Débito inválido");
                default:
                    // 401/403/409/5xx: no es una respuesta sobre el débito en sí. Queda PENDIENTE.
                    log.LogWarning("AccountService respondió {Status} al débito del pago {PagoId}: {Problema}",
                        (int)resp.StatusCode, pago.Id, problema);
                    throw new CuentasNoDisponibleException(502,
                        $"AccountService respondió {(int)resp.StatusCode} al débito.",
                        tokenRechazado: resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
            }
        }
    }

    private static async Task<string?> LeerProblemaAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        try
        {
            var pd = await resp.Content.ReadFromJsonAsync<ProblemDetails>(ct);
            return pd?.Title;
        }
        catch
        {
            return null;
        }
    }
}
