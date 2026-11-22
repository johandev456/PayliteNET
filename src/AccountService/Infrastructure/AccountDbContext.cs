using AccountService.Domain;
using Microsoft.EntityFrameworkCore;

namespace AccountService.Infrastructure;

public class AccountDbContext(DbContextOptions<AccountDbContext> options) : DbContext(options)
{
    public const string UxMovimientoOperacion = "ux_movimiento_cuenta_operacion";

    public DbSet<Cuenta> Cuentas => Set<Cuenta>();
    public DbSet<Movimiento> Movimientos => Set<Movimiento>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Cuenta>(e =>
        {
            e.ToTable("cuenta", t => t.HasCheckConstraint("ck_cuenta_saldo_no_negativo", "saldo >= 0"));
            e.HasKey(c => c.Id);
            e.Property(c => c.Id).HasColumnName("id");
            e.Property(c => c.OwnerId).HasColumnName("owner_id").IsRequired();
            e.Property(c => c.Saldo).HasColumnName("saldo").HasColumnType("numeric(19,2)").HasDefaultValue(0m);
            e.Property(c => c.Moneda).HasColumnName("moneda").HasMaxLength(3).IsFixedLength(false).IsRequired();
            e.Property(c => c.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            // xmin: columna de sistema de PostgreSQL, cambia en cada UPDATE. Bloqueo optimista sin columna extra.
            e.Property(c => c.Version).IsRowVersion();
            e.HasIndex(c => c.OwnerId).HasDatabaseName("ix_cuenta_owner_id");
        });

        modelBuilder.Entity<Movimiento>(e =>
        {
            e.ToTable("movimiento", t => t.HasCheckConstraint("ck_movimiento_monto_positivo", "monto > 0"));
            e.HasKey(m => m.Id);
            e.Property(m => m.Id).HasColumnName("id");
            e.Property(m => m.CuentaId).HasColumnName("cuenta_id");
            e.Property(m => m.Tipo).HasColumnName("tipo").HasMaxLength(10).HasConversion(
                t => t.ToString().ToUpperInvariant(),
                s => Enum.Parse<TipoMovimiento>(s, true));
            e.Property(m => m.Monto).HasColumnName("monto").HasColumnType("numeric(19,2)");
            e.Property(m => m.OperacionId).HasColumnName("operacion_id").HasMaxLength(Movimiento.MaxLongitudOperacionId).IsRequired();
            e.Property(m => m.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            // Mismo servicio, misma base: aquí sí hay FK.
            e.HasOne<Cuenta>().WithMany().HasForeignKey(m => m.CuentaId).OnDelete(DeleteBehavior.Restrict);
            // La base de la idempotencia: una operación se aplica una sola vez por cuenta.
            e.HasIndex(m => new { m.CuentaId, m.OperacionId }).IsUnique().HasDatabaseName(UxMovimientoOperacion);
        });
    }
}
