using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoalFlow.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class EntregasEventos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "entregas_eventos",
                schema: "goalflow",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo_de_evento = table.Column<string>(type: "text", nullable: false),
                    agregado_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_del_agregado = table.Column<int>(type: "integer", nullable: false),
                    consumidor = table.Column<string>(type: "text", nullable: false),
                    estado = table.Column<string>(type: "text", nullable: false),
                    intentos = table.Column<int>(type: "integer", nullable: false),
                    proximo_intento_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    token_del_lease = table.Column<Guid>(type: "uuid", nullable: true),
                    lease_vence_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ultimo_error = table.Column<string>(type: "text", nullable: true),
                    creado_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    completado_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entregas_eventos", x => x.id);
                    table.ForeignKey(
                        name: "fk_entregas_eventos_ligas",
                        column: x => x.tenant_id,
                        principalSchema: "goalflow",
                        principalTable: "ligas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_entregas_eventos_leases",
                schema: "goalflow",
                table: "entregas_eventos",
                columns: new[] { "tenant_id", "estado", "lease_vence_en" });

            migrationBuilder.CreateIndex(
                name: "ix_entregas_eventos_pendientes",
                schema: "goalflow",
                table: "entregas_eventos",
                columns: new[] { "tenant_id", "estado", "proximo_intento_en" });

            migrationBuilder.CreateIndex(
                name: "ux_entregas_eventos_clave_logica",
                schema: "goalflow",
                table: "entregas_eventos",
                columns: new[] { "tenant_id", "tipo_de_evento", "agregado_id", "version_del_agregado", "consumidor" },
                unique: true);

            // RLS en la misma migración que la tabla (ADR-14 §2.2); la política es la misma que la de equipos.
            migrationBuilder.Sql("""
                ALTER TABLE goalflow.entregas_eventos ENABLE ROW LEVEL SECURITY;
                CREATE POLICY entregas_eventos_tenant ON goalflow.entregas_eventos
                    FOR ALL
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                """);

            // Los default privileges de db/roles/001_roles.sql conceden DELETE sobre toda tabla nueva (Spec 004 R-12).
            migrationBuilder.Sql("""
                REVOKE DELETE ON goalflow.entregas_eventos FROM goalflow_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "entregas_eventos",
                schema: "goalflow");
        }
    }
}
