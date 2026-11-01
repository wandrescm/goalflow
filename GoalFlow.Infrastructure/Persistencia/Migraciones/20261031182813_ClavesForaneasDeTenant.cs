using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoalFlow.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class ClavesForaneasDeTenant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_admins_liga_tenant_id",
                schema: "goalflow",
                table: "admins_liga",
                column: "tenant_id");

            migrationBuilder.AddForeignKey(
                name: "fk_admins_liga_ligas",
                schema: "goalflow",
                table: "admins_liga",
                column: "tenant_id",
                principalSchema: "goalflow",
                principalTable: "ligas",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_equipos_ligas",
                schema: "goalflow",
                table: "equipos",
                column: "tenant_id",
                principalSchema: "goalflow",
                principalTable: "ligas",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_admins_liga_ligas",
                schema: "goalflow",
                table: "admins_liga");

            migrationBuilder.DropForeignKey(
                name: "fk_equipos_ligas",
                schema: "goalflow",
                table: "equipos");

            migrationBuilder.DropIndex(
                name: "ix_admins_liga_tenant_id",
                schema: "goalflow",
                table: "admins_liga");
        }
    }
}
