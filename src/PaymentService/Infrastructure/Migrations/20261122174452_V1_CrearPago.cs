using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class V1_CrearPago : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pago",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<string>(type: "text", nullable: false),
                    cuenta_id = table.Column<Guid>(type: "uuid", nullable: false),
                    monto = table.Column<decimal>(type: "numeric(19,2)", nullable: false),
                    comision = table.Column<decimal>(type: "numeric(19,2)", nullable: false),
                    metodo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    motivo_rechazo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    intentos = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pago", x => x.id);
                    table.CheckConstraint("ck_pago_comision_no_negativa", "comision >= 0");
                    table.CheckConstraint("ck_pago_estado", "estado IN ('PENDIENTE','APROBADO','RECHAZADO','REVISION_MANUAL')");
                    table.CheckConstraint("ck_pago_monto_positivo", "monto > 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_pago_owner_created",
                table: "pago",
                columns: new[] { "owner_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_pago_pendientes",
                table: "pago",
                column: "created_at",
                filter: "estado = 'PENDIENTE'");

            migrationBuilder.CreateIndex(
                name: "ux_pago_owner_idempotency_key",
                table: "pago",
                columns: new[] { "owner_id", "idempotency_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pago");
        }
    }
}
