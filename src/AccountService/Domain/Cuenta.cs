namespace AccountService.Domain;

/// <summary>
/// Cuenta con saldo. Nace válida (saldo 0, fecha asignada) y su saldo solo cambia
/// con <see cref="Debitar"/> y <see cref="Acreditar"/>: no hay setter público.
/// </summary>
public class Cuenta
{
    public Guid Id { get; private set; }
    public string OwnerId { get; private set; } = null!;
    public decimal Saldo { get; private set; }
    public string Moneda { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Concurrency token: se mapea a la columna de sistema <c>xmin</c> de PostgreSQL.
    /// Si dos débitos leen la misma versión, el segundo UPDATE no encuentra la fila
    /// y EF lanza DbUpdateConcurrencyException (sin lost update).
    /// </summary>
    public uint Version { get; private set; }

    // Para EF Core.
    private Cuenta() { }

    public Cuenta(string ownerId, string moneda)
    {
        if (string.IsNullOrWhiteSpace(ownerId))
            throw new ArgumentException("El dueño es obligatorio.", nameof(ownerId));

        Id = Guid.NewGuid();
        OwnerId = ownerId;
        Moneda = Montos.ValidarMoneda(moneda);
        Saldo = 0m;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public void Debitar(decimal monto)
    {
        Montos.ValidarMonto(monto);
        if (Saldo < monto)
            throw new SaldoInsuficienteException(Id);
        Saldo -= monto;
    }

    public void Acreditar(decimal monto)
    {
        Montos.ValidarMonto(monto);
        if (Saldo + monto > Montos.Maximo)
            throw new EntradaInvalidaException("El saldo resultante excede el máximo permitido.");
        Saldo += monto;
    }
}
