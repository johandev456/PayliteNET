namespace PaymentService.Domain.Metodos;

/// <summary>
/// Factory: recibe todas las estrategias registradas en DI (<c>IEnumerable&lt;IMetodoPago&gt;</c>)
/// y las indexa por código. No conoce ninguna implementación concreta.
/// </summary>
public sealed class MetodoPagoFactory
{
    private readonly Dictionary<string, IMetodoPago> _metodos;

    public MetodoPagoFactory(IEnumerable<IMetodoPago> metodos)
    {
        // ToDictionary falla al arrancar si dos estrategias comparten código: mejor que en un pago.
        _metodos = metodos.ToDictionary(m => m.Codigo, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<string> Codigos => _metodos.Keys;

    public IMetodoPago Obtener(string? codigo) =>
        codigo is not null && _metodos.TryGetValue(codigo.Trim(), out var metodo)
            ? metodo
            : throw new MetodoPagoNoSoportadoException(codigo);
}
