using AccountService.Domain;
using Microsoft.EntityFrameworkCore;

namespace AccountService.Infrastructure;

public class AccountDbContext(DbContextOptions<AccountDbContext> options) : DbContext(options)
{
    public DbSet<Cuenta> Cuentas => Set<Cuenta>();

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
    }
}
