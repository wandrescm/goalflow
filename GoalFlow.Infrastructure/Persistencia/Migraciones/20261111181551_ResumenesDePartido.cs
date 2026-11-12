using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoalFlow.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class ResumenesDePartido : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "llamadas_ia",
                schema: "goalflow",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    torneo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    partido_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    huella = table.Column<string>(type: "text", nullable: false),
                    numero = table.Column<int>(type: "integer", nullable: false),
                    intento_entrega = table.Column<int>(type: "integer", nullable: false),
                    mes = table.Column<string>(type: "text", nullable: false),
                    reserva = table.Column<long>(type: "bigint", nullable: false),
                    estado = table.Column<string>(type: "text", nullable: false),
                    tokens_entrada = table.Column<int>(type: "integer", nullable: true),
                    tokens_salida = table.Column<int>(type: "integer", nullable: true),
                    costo = table.Column<long>(type: "bigint", nullable: true),
                    resultado = table.Column<string>(type: "text", nullable: true),
                    conciliada_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    creado_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_llamadas_ia", x => x.id);
                    table.UniqueConstraint("ak_llamadas_ia_tenant_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_llamadas_ia_conciliada_en_segun_estado", "(estado = 'Conciliada') = (conciliada_en is not null)");
                    table.CheckConstraint("ck_llamadas_ia_costo", "costo is null or costo >= 0");
                    table.CheckConstraint("ck_llamadas_ia_costo_segun_estado", "(estado = 'Conciliada') = (costo is not null)");
                    table.CheckConstraint("ck_llamadas_ia_estado", "estado in ('Reservada', 'Conciliada')");
                    table.CheckConstraint("ck_llamadas_ia_intento_entrega", "intento_entrega >= 1");
                    table.CheckConstraint("ck_llamadas_ia_mes", "mes ~ '^[0-9]{4}-(0[1-9]|1[0-2])$'");
                    table.CheckConstraint("ck_llamadas_ia_numero", "numero >= 1");
                    table.CheckConstraint("ck_llamadas_ia_reserva", "reserva > 0");
                    table.CheckConstraint("ck_llamadas_ia_resultado", "resultado is null or char_length(resultado) between 1 and 64");
                    table.CheckConstraint("ck_llamadas_ia_resultado_segun_estado", "(estado = 'Conciliada') = (resultado is not null)");
                    table.CheckConstraint("ck_llamadas_ia_tokens", "coalesce(tokens_entrada, 0) >= 0 and coalesce(tokens_salida, 0) >= 0");
                    table.CheckConstraint("ck_llamadas_ia_tokens_entrada_segun_estado", "(estado = 'Conciliada') = (tokens_entrada is not null)");
                    table.CheckConstraint("ck_llamadas_ia_tokens_salida_segun_estado", "(estado = 'Conciliada') = (tokens_salida is not null)");
                    table.CheckConstraint("ck_llamadas_ia_version", "version >= 1");
                    table.ForeignKey(
                        name: "fk_llamadas_ia_partidos",
                        columns: x => new { x.tenant_id, x.torneo_id, x.partido_id },
                        principalSchema: "goalflow",
                        principalTable: "partidos",
                        principalColumns: new[] { "tenant_id", "torneo_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "presupuestos_ia",
                schema: "goalflow",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mes = table.Column<string>(type: "text", nullable: false),
                    tope = table.Column<long>(type: "bigint", nullable: false),
                    reservado = table.Column<long>(type: "bigint", nullable: false),
                    consumido = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_presupuestos_ia", x => x.id);
                    table.CheckConstraint("ck_presupuestos_ia_consumido", "consumido >= 0");
                    table.CheckConstraint("ck_presupuestos_ia_mes", "mes ~ '^[0-9]{4}-(0[1-9]|1[0-2])$'");
                    table.CheckConstraint("ck_presupuestos_ia_reservado", "reservado >= 0");
                    table.CheckConstraint("ck_presupuestos_ia_tope", "tope > 0");
                    table.ForeignKey(
                        name: "fk_presupuestos_ia_ligas",
                        column: x => x.tenant_id,
                        principalSchema: "goalflow",
                        principalTable: "ligas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "resumenes_partido",
                schema: "goalflow",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    torneo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    partido_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_origen = table.Column<int>(type: "integer", nullable: false),
                    huella = table.Column<string>(type: "text", nullable: false),
                    origen = table.Column<string>(type: "text", nullable: false),
                    estado = table.Column<string>(type: "text", nullable: false),
                    motivo_borrador = table.Column<string>(type: "text", nullable: true),
                    texto = table.Column<string>(type: "text", nullable: false),
                    modelo = table.Column<string>(type: "text", nullable: true),
                    version_prompt = table.Column<string>(type: "text", nullable: true),
                    llamada_id = table.Column<Guid>(type: "uuid", nullable: true),
                    creado_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_resumenes_partido", x => x.id);
                    table.CheckConstraint("ck_resumenes_partido_estado", "estado in ('PorVerificar', 'Borrador')");
                    table.CheckConstraint("ck_resumenes_partido_huella", "huella ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_resumenes_partido_llamada_segun_origen", "(origen = 'Modelo') = (llamada_id is not null)");
                    table.CheckConstraint("ck_resumenes_partido_modelo_segun_origen", "(origen = 'Modelo') = (modelo is not null)");
                    table.CheckConstraint("ck_resumenes_partido_motivo", "motivo_borrador is null or motivo_borrador in ('presupuesto', 'limite_de_llamadas', 'solicitud_invalida', 'entrada_excedida', 'rechazo_del_modelo')");
                    table.CheckConstraint("ck_resumenes_partido_motivo_segun_estado", "(estado = 'Borrador') = (motivo_borrador is not null)");
                    table.CheckConstraint("ck_resumenes_partido_origen", "origen in ('Modelo', 'Plantilla')");
                    table.CheckConstraint("ck_resumenes_partido_texto", "char_length(texto) between 1 and 4000");
                    table.CheckConstraint("ck_resumenes_partido_version_origen", "version_origen >= 1");
                    table.CheckConstraint("ck_resumenes_partido_version_prompt_segun_origen", "(origen = 'Modelo') = (version_prompt is not null)");
                    table.ForeignKey(
                        name: "fk_resumenes_partido_llamadas",
                        columns: x => new { x.tenant_id, x.llamada_id },
                        principalSchema: "goalflow",
                        principalTable: "llamadas_ia",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_resumenes_partido_partidos",
                        columns: x => new { x.tenant_id, x.torneo_id, x.partido_id },
                        principalSchema: "goalflow",
                        principalTable: "partidos",
                        principalColumns: new[] { "tenant_id", "torneo_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_llamadas_ia_tenant_id_torneo_id_partido_id",
                schema: "goalflow",
                table: "llamadas_ia",
                columns: new[] { "tenant_id", "torneo_id", "partido_id" });

            migrationBuilder.CreateIndex(
                name: "ux_llamadas_ia_tenant_partido_huella_numero",
                schema: "goalflow",
                table: "llamadas_ia",
                columns: new[] { "tenant_id", "partido_id", "huella", "numero" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_presupuestos_ia_tenant_mes",
                schema: "goalflow",
                table: "presupuestos_ia",
                columns: new[] { "tenant_id", "mes" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_resumenes_partido_tenant_id_llamada_id",
                schema: "goalflow",
                table: "resumenes_partido",
                columns: new[] { "tenant_id", "llamada_id" });

            migrationBuilder.CreateIndex(
                name: "IX_resumenes_partido_tenant_id_torneo_id_partido_id",
                schema: "goalflow",
                table: "resumenes_partido",
                columns: new[] { "tenant_id", "torneo_id", "partido_id" });

            migrationBuilder.CreateIndex(
                name: "ux_resumenes_partido_tenant_partido_huella",
                schema: "goalflow",
                table: "resumenes_partido",
                columns: new[] { "tenant_id", "partido_id", "huella" },
                unique: true);

            // RLS en la misma migración que cada tabla, una política por tabla (ADR-14 §2.2, Spec 007 R-04).
            foreach (var tabla in new[] { "resumenes_partido", "llamadas_ia", "presupuestos_ia" })
            {
                migrationBuilder.Sql($"""
                    ALTER TABLE goalflow.{tabla} ENABLE ROW LEVEL SECURITY;
                    CREATE POLICY {tabla}_tenant ON goalflow.{tabla}
                        FOR ALL
                        USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                        WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                    """);
            }

            // Privilegios exactos (Spec 007 R-04). Los default privileges de db/roles/001_roles.sql conceden las cuatro
            // operaciones sobre toda tabla nueva, así que se retiran y se concede solo lo que corresponde: los resúmenes son
            // inmutables (SELECT, INSERT); las llamadas y los contadores se actualizan (SELECT, INSERT, UPDATE). Nunca DELETE.
            migrationBuilder.Sql("""
                REVOKE ALL ON goalflow.resumenes_partido, goalflow.llamadas_ia, goalflow.presupuestos_ia FROM goalflow_app;
                GRANT SELECT, INSERT ON goalflow.resumenes_partido TO goalflow_app;
                GRANT SELECT, INSERT, UPDATE ON goalflow.llamadas_ia, goalflow.presupuestos_ia TO goalflow_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "presupuestos_ia",
                schema: "goalflow");

            migrationBuilder.DropTable(
                name: "resumenes_partido",
                schema: "goalflow");

            migrationBuilder.DropTable(
                name: "llamadas_ia",
                schema: "goalflow");
        }
    }
}
