using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoalFlow.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class PlanillaPostPartido : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_partidos_estado",
                schema: "goalflow",
                table: "partidos");

            migrationBuilder.AddColumn<int>(
                name: "version",
                schema: "goalflow",
                table: "partidos",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "goalflow",
                table: "partidos",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddUniqueConstraint(
                name: "ak_partidos_tenant_torneo_id",
                schema: "goalflow",
                table: "partidos",
                columns: new[] { "tenant_id", "torneo_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_inscripciones_jugador_tenant_torneo_id",
                schema: "goalflow",
                table: "inscripciones_jugador",
                columns: new[] { "tenant_id", "torneo_id", "id" });

            migrationBuilder.CreateTable(
                name: "planillas",
                schema: "goalflow",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    torneo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    partido_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    estado = table.Column<string>(type: "text", nullable: false),
                    marcador_local = table.Column<int>(type: "integer", nullable: true),
                    marcador_visitante = table.Column<int>(type: "integer", nullable: true),
                    oficial_local = table.Column<int>(type: "integer", nullable: true),
                    oficial_visitante = table.Column<int>(type: "integer", nullable: true),
                    motivo = table.Column<string>(type: "text", nullable: true),
                    motivo_rectificacion = table.Column<string>(type: "text", nullable: true),
                    tarjetas_informadas = table.Column<bool>(type: "boolean", nullable: true),
                    guardado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    guardado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_planillas", x => x.id);
                    table.UniqueConstraint("ak_planillas_tenant_torneo_id", x => new { x.tenant_id, x.torneo_id, x.id });
                    table.CheckConstraint("ck_planillas_estado", "estado in ('Programado', 'Jugado', 'Suspendido', 'Adjudicado')");
                    table.CheckConstraint("ck_planillas_marcador", "(marcador_local is null and marcador_visitante is null) or (marcador_local between 0 and 99 and marcador_visitante between 0 and 99)");
                    table.CheckConstraint("ck_planillas_motivo", "motivo is null or motivo in ('Clima', 'Luz', 'Disturbios', 'WalkOver', 'Sancion', 'Otro')");
                    table.CheckConstraint("ck_planillas_motivo_rectificacion", "motivo_rectificacion is null or motivo_rectificacion in ('ErrorDeRegistro', 'PartidoEquivocado', 'DecisionReglamentaria', 'Otro')");
                    table.CheckConstraint("ck_planillas_oficial", "(oficial_local is null and oficial_visitante is null) or (oficial_local between 0 and 99 and oficial_visitante between 0 and 99)");
                    table.CheckConstraint("ck_planillas_version", "version >= 1");
                    table.ForeignKey(
                        name: "fk_planillas_partidos",
                        columns: x => new { x.tenant_id, x.torneo_id, x.partido_id },
                        principalSchema: "goalflow",
                        principalTable: "partidos",
                        principalColumns: new[] { "tenant_id", "torneo_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "goles",
                schema: "goalflow",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    torneo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    planilla_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "text", nullable: false),
                    equipo_torneo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    inscripcion_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cantidad = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_goles", x => x.id);
                    table.CheckConstraint("ck_goles_cantidad", "cantidad >= 1");
                    table.CheckConstraint("ck_goles_inscripcion_segun_tipo", "(tipo = 'SinAutor') = (inscripcion_id is null)");
                    table.CheckConstraint("ck_goles_tipo", "tipo in ('Jugador', 'Autogol', 'SinAutor')");
                    table.ForeignKey(
                        name: "fk_goles_equipos_torneo",
                        columns: x => new { x.tenant_id, x.torneo_id, x.equipo_torneo_id },
                        principalSchema: "goalflow",
                        principalTable: "equipos_torneo",
                        principalColumns: new[] { "tenant_id", "torneo_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_goles_inscripciones_jugador",
                        columns: x => new { x.tenant_id, x.torneo_id, x.inscripcion_id },
                        principalSchema: "goalflow",
                        principalTable: "inscripciones_jugador",
                        principalColumns: new[] { "tenant_id", "torneo_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_goles_planillas",
                        columns: x => new { x.tenant_id, x.torneo_id, x.planilla_id },
                        principalSchema: "goalflow",
                        principalTable: "planillas",
                        principalColumns: new[] { "tenant_id", "torneo_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tarjetas",
                schema: "goalflow",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    torneo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    planilla_id = table.Column<Guid>(type: "uuid", nullable: false),
                    inscripcion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    color = table.Column<string>(type: "text", nullable: false),
                    cantidad = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tarjetas", x => x.id);
                    table.CheckConstraint("ck_tarjetas_cantidad", "cantidad between 1 and 9");
                    table.CheckConstraint("ck_tarjetas_color", "color in ('Amarilla', 'Azul', 'Roja')");
                    table.ForeignKey(
                        name: "fk_tarjetas_inscripciones_jugador",
                        columns: x => new { x.tenant_id, x.torneo_id, x.inscripcion_id },
                        principalSchema: "goalflow",
                        principalTable: "inscripciones_jugador",
                        principalColumns: new[] { "tenant_id", "torneo_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tarjetas_planillas",
                        columns: x => new { x.tenant_id, x.torneo_id, x.planilla_id },
                        principalSchema: "goalflow",
                        principalTable: "planillas",
                        principalColumns: new[] { "tenant_id", "torneo_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_partidos_estado",
                schema: "goalflow",
                table: "partidos",
                sql: "estado in ('Programado', 'Cancelado', 'Jugado', 'Suspendido', 'Adjudicado')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_partidos_version",
                schema: "goalflow",
                table: "partidos",
                sql: "version >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_goles_tenant_id_torneo_id_equipo_torneo_id",
                schema: "goalflow",
                table: "goles",
                columns: new[] { "tenant_id", "torneo_id", "equipo_torneo_id" });

            migrationBuilder.CreateIndex(
                name: "IX_goles_tenant_id_torneo_id_inscripcion_id",
                schema: "goalflow",
                table: "goles",
                columns: new[] { "tenant_id", "torneo_id", "inscripcion_id" });

            migrationBuilder.CreateIndex(
                name: "IX_goles_tenant_id_torneo_id_planilla_id",
                schema: "goalflow",
                table: "goles",
                columns: new[] { "tenant_id", "torneo_id", "planilla_id" });

            migrationBuilder.CreateIndex(
                name: "ux_goles_tenant_planilla_tipo_equipo_inscripcion",
                schema: "goalflow",
                table: "goles",
                columns: new[] { "tenant_id", "planilla_id", "tipo", "equipo_torneo_id", "inscripcion_id" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_planillas_tenant_id_torneo_id_partido_id",
                schema: "goalflow",
                table: "planillas",
                columns: new[] { "tenant_id", "torneo_id", "partido_id" });

            migrationBuilder.CreateIndex(
                name: "ux_planillas_tenant_partido_version",
                schema: "goalflow",
                table: "planillas",
                columns: new[] { "tenant_id", "partido_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tarjetas_tenant_id_torneo_id_inscripcion_id",
                schema: "goalflow",
                table: "tarjetas",
                columns: new[] { "tenant_id", "torneo_id", "inscripcion_id" });

            migrationBuilder.CreateIndex(
                name: "IX_tarjetas_tenant_id_torneo_id_planilla_id",
                schema: "goalflow",
                table: "tarjetas",
                columns: new[] { "tenant_id", "torneo_id", "planilla_id" });

            migrationBuilder.CreateIndex(
                name: "ux_tarjetas_tenant_planilla_inscripcion_color",
                schema: "goalflow",
                table: "tarjetas",
                columns: new[] { "tenant_id", "planilla_id", "inscripcion_id", "color" },
                unique: true);

            // RLS en la misma migración que cada tabla (ADR-14 §2.2, Spec 006 R-06); la política es la de las demás tablas.
            foreach (var tabla in new[] { "planillas", "goles", "tarjetas" })
            {
                migrationBuilder.Sql($"""
                    ALTER TABLE goalflow.{tabla} ENABLE ROW LEVEL SECURITY;
                    CREATE POLICY {tabla}_tenant ON goalflow.{tabla}
                        FOR ALL
                        USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                        WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                    """);
            }

            // Las versiones son inmutables (Spec 006 R-06, R-21): solo SELECT e INSERT. Los default privileges de
            // db/roles/001_roles.sql conceden las cuatro operaciones sobre toda tabla nueva, así que se retiran aquí.
            migrationBuilder.Sql("""
                REVOKE ALL ON goalflow.planillas, goalflow.goles, goalflow.tarjetas FROM goalflow_app;
                GRANT SELECT, INSERT ON goalflow.planillas, goalflow.goles, goalflow.tarjetas TO goalflow_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "goles",
                schema: "goalflow");

            migrationBuilder.DropTable(
                name: "tarjetas",
                schema: "goalflow");

            migrationBuilder.DropTable(
                name: "planillas",
                schema: "goalflow");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_partidos_tenant_torneo_id",
                schema: "goalflow",
                table: "partidos");

            migrationBuilder.DropCheckConstraint(
                name: "ck_partidos_estado",
                schema: "goalflow",
                table: "partidos");

            migrationBuilder.DropCheckConstraint(
                name: "ck_partidos_version",
                schema: "goalflow",
                table: "partidos");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_inscripciones_jugador_tenant_torneo_id",
                schema: "goalflow",
                table: "inscripciones_jugador");

            migrationBuilder.DropColumn(
                name: "version",
                schema: "goalflow",
                table: "partidos");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "goalflow",
                table: "partidos");

            migrationBuilder.AddCheckConstraint(
                name: "ck_partidos_estado",
                schema: "goalflow",
                table: "partidos",
                sql: "estado in ('Programado', 'Cancelado')");
        }
    }
}
