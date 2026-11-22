namespace PaymentService.Security;

/// <summary>
/// El dueño de un recurso es siempre el <c>sub</c> del token validado.
/// Nunca se acepta un id de usuario enviado por el cliente (header, query o body).
/// </summary>
public interface ICurrentUser
{
    string Id { get; }
}

public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public string Id =>
        accessor.HttpContext?.User.FindFirst("sub")?.Value
        ?? throw new InvalidOperationException("Petición sin usuario autenticado (falta el claim sub).");
}
