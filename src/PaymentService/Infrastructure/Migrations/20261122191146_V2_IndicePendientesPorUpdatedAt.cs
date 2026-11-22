using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class V2_IndicePendientesPorUpdatedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_pago_pendientes",
                table: "pago");

            migrationBuilder.CreateIndex(
                name: "ix_pago_pendientes_updated_at",
                table: "pago",
                column: "updated_at",
                filter: "estado = 'PENDIENTE'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_pago_pendientes_updated_at",
                table: "pago");

            migrationBuilder.CreateIndex(
                name: "ix_pago_pendientes",
                table: "pago",
                column: "created_at",
                filter: "estado = 'PENDIENTE'");
        }
    }
}
