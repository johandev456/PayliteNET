using AccountService.Application;
using AccountService.Api.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace AccountService.Api;

public static class IdempotencyHeaders
{
    public const string Key = "Idempotency-Key";
    public const string Replayed = "Idempotent-Replayed";

    /// <summary>200 en ambos casos; el header indica si la operación ya se había aplicado antes.</summary>
    public static ActionResult<MovimientoResponse> Responder(this ControllerBase c, ResultadoMovimiento r)
    {
        if (r.Replay)
            c.Response.Headers[Replayed] = "true";
        return c.Ok(MovimientoResponse.De(r));
    }
}
