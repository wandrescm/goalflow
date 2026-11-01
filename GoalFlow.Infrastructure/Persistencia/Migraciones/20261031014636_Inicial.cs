using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoalFlow.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "goalflow");

            migrationBuilder.CreateTable(
                name: "admins_liga",
                schema: "goalflow",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rol = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_admins_liga", x => new { x.user_id, x.tenant_id });
                });

            migrationBuilder.CreateTable(
                name: "equipos",
                schema: "goalflow",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_equipos", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ligas",
                schema: "goalflow",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "text", nullable: false),
                    nombre = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ligas", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_equipos_tenant_id",
                schema: "goalflow",
                table: "equipos",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ux_ligas_slug",
                schema: "goalflow",
                table: "ligas",
                column: "slug",
                unique: true);

            // RLS y revocaciones en la misma migración que las tablas (ADR-14 §2.2, §2.7 y §2.10).
            // nullif: tras una transacción, current_setting devuelve '' y convertirlo a uuid daría error.
            migrationBuilder.Sql("""
                ALTER TABLE goalflow.equipos ENABLE ROW LEVEL SECURITY;
                CREATE POLICY equipos_tenant ON goalflow.equipos
                    FOR ALL
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                """);

            migrationBuilder.Sql("""
                ALTER TABLE goalflow.admins_liga ENABLE ROW LEVEL SECURITY;
                CREATE POLICY admins_liga_identidad ON goalflow.admins_liga
                    FOR SELECT
                    USING (user_id = nullif(current_setting('app.user_id', true), '')::uuid);
                """);

            // Los default privileges de db/roles/001_roles.sql conceden DML sobre toda tabla nueva.
            migrationBuilder.Sql("""
                REVOKE INSERT, UPDATE, DELETE ON goalflow.ligas FROM goalflow_app;
                REVOKE INSERT, UPDATE, DELETE ON goalflow.admins_liga FROM goalflow_app;
                REVOKE ALL ON goalflow."__EFMigrationsHistory" FROM goalflow_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "admins_liga",
                schema: "goalflow");

            migrationBuilder.DropTable(
                name: "equipos",
                schema: "goalflow");

            migrationBuilder.DropTable(
                name: "ligas",
                schema: "goalflow");
        }
    }
}
