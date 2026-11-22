using PaymentService.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace PaymentService.Api;

/// <summary>
/// Único lugar que traduce excepciones a HTTP. Los errores de dominio llevan su código;
/// cualquier otra cosa es 500 sin detalles internos (el stack trace queda solo en el log).
/// </summary>
public sealed class GlobalExceptionHandler(IProblemDetailsService problemDetails, ILogger<GlobalExceptionHandler> log)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        var (status, titulo, detalle) = ex switch
        {
            DomainException d => (d.Status, d.Titulo, d.Message),
            BadHttpRequestException => (400, "Petición inválida", "El cuerpo o los parámetros de la petición no son válidos."),
            _ => (500, "Error interno", "Ocurrió un error inesperado."),
        };

        if (ex is not DomainException)
            log.LogError(ex, "Error no controlado en {Metodo} {Ruta}", ctx.Request.Method, ctx.Request.Path);

        var pd = new ProblemDetails { Status = status, Title = titulo, Detail = detalle };
        if (ex is DomainException dom)
            foreach (var (k, v) in dom.Extras)
                pd.Extensions[k] = v;

        ctx.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext { HttpContext = ctx, ProblemDetails = pd });
    }
}
