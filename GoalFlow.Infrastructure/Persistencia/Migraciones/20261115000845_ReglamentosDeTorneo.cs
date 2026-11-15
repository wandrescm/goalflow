using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoalFlow.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class ReglamentosDeTorneo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "reglamentos_torneo",
                schema: "goalflow",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    torneo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    estado = table.Column<string>(type: "text", nullable: false),
                    contenido = table.Column<string>(type: "jsonb", nullable: false),
                    guardado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    guardado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reglamentos_torneo", x => x.id);
                    table.CheckConstraint("ck_reglamentos_torneo_contenido", "jsonb_typeof(contenido) = 'object'");
                    table.CheckConstraint("ck_reglamentos_torneo_estado", "estado in ('Provisional', 'Confirmado')");
                    table.CheckConstraint("ck_reglamentos_torneo_version", "version >= 1");
                    table.ForeignKey(
                        name: "fk_reglamentos_torneo_torneos",
                        columns: x => new { x.tenant_id, x.torneo_id },
                        principalSchema: "goalflow",
                        principalTable: "torneos",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_reglamentos_torneo_tenant_torneo_version",
                schema: "goalflow",
                table: "reglamentos_torneo",
                columns: new[] { "tenant_id", "torneo_id", "version" },
                unique: true);

            // RLS en la misma migración que la tabla (ADR-14 §2.2, Spec 009 R-02); la política es la de las demás tablas.
            migrationBuilder.Sql("""
                ALTER TABLE goalflow.reglamentos_torneo ENABLE ROW LEVEL SECURITY;
                CREATE POLICY reglamentos_torneo_tenant ON goalflow.reglamentos_torneo
                    FOR ALL
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                """);

            // Las versiones son inmutables (Spec 009 R-02): solo SELECT e INSERT. Los default privileges de
            // db/roles/001_roles.sql conceden las cuatro operaciones sobre toda tabla nueva, así que se retiran aquí.
            migrationBuilder.Sql("""
                REVOKE ALL ON goalflow.reglamentos_torneo FROM goalflow_app;
                GRANT SELECT, INSERT ON goalflow.reglamentos_torneo TO goalflow_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reglamentos_torneo",
                schema: "goalflow");
        }
    }
}
