using Microsoft.EntityFrameworkCore;
using PaymentService.Domain;

namespace PaymentService.Infrastructure;

public class PaymentDbContext(DbContextOptions<PaymentDbContext> options) : DbContext(options)
{
    public DbSet<Pago> Pagos => Set<Pago>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Pago>(e =>
        {
            e.ToTable("pago", t =>
            {
                t.HasCheckConstraint("ck_pago_monto_positivo", "monto > 0");
                t.HasCheckConstraint("ck_pago_comision_no_negativa", "comision >= 0");
                t.HasCheckConstraint("ck_pago_estado",
                    "estado IN ('PENDIENTE','APROBADO','RECHAZADO','REVISION_MANUAL')");
            });
            e.HasKey(p => p.Id);
            e.Property(p => p.Id).HasColumnName("id");
            e.Property(p => p.OwnerId).HasColumnName("owner_id").IsRequired();
            // Sin FK: la cuenta vive en la base de otro servicio.
            e.Property(p => p.CuentaId).HasColumnName("cuenta_id");
            e.Property(p => p.Monto).HasColumnName("monto").HasColumnType("numeric(19,2)");
            e.Property(p => p.Comision).HasColumnName("comision").HasColumnType("numeric(19,2)");
            e.Property(p => p.Metodo).HasColumnName("metodo").HasMaxLength(20).IsRequired();
            e.Property(p => p.Estado).HasColumnName("estado").HasMaxLength(20).HasConversion<string>();
            e.Property(p => p.IdempotencyKey).HasColumnName("idempotency_key")
                .HasMaxLength(Pago.MaxLongitudIdempotencyKey).IsRequired();
            e.Property(p => p.MotivoRechazo).HasColumnName("motivo_rechazo").HasMaxLength(500);
            e.Property(p => p.Intentos).HasColumnName("intentos").HasDefaultValue(0);
            e.Property(p => p.CreatedAt).HasColumnName("created_at");
            e.Property(p => p.UpdatedAt).HasColumnName("updated_at");
            e.Property(p => p.Version).IsRowVersion();
            e.Ignore(p => p.Total);

            e.HasIndex(p => new { p.OwnerId, p.IdempotencyKey }).IsUnique()
                .HasDatabaseName("ux_pago_owner_idempotency_key");
            e.HasIndex(p => new { p.OwnerId, p.CreatedAt }).HasDatabaseName("ix_pago_owner_created");
            // Índice parcial: la recuperación solo mira pendientes.
            e.HasIndex(p => p.CreatedAt).HasFilter("estado = 'PENDIENTE'").HasDatabaseName("ix_pago_pendientes");
        });
    }
}
