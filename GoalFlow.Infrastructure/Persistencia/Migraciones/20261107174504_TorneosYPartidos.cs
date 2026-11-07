using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoalFlow.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class TorneosYPartidos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "codigo",
                schema: "goalflow",
                table: "equipos",
                type: "text",
                nullable: false);
            // Sin valor por defecto: la tabla equipos está vacía en producción (Spec 005 R-03), y un código
            // implícito violaría la unicidad por tenant.

            migrationBuilder.AddUniqueConstraint(
                name: "ak_equipos_tenant_id_id",
                schema: "goalflow",
                table: "equipos",
                columns: new[] { "tenant_id", "id" });

            migrationBuilder.CreateTable(
                name: "jugadores",
                schema: "goalflow",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "text", nullable: false),
                    nombre = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_jugadores", x => x.id);
                    table.UniqueConstraint("ak_jugadores_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_jugadores_ligas",
                        column: x => x.tenant_id,
                        principalSchema: "goalflow",
                        principalTable: "ligas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "torneos",
                schema: "goalflow",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "text", nullable: false),
                    nombre = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_torneos", x => x.id);
                    table.UniqueConstraint("ak_torneos_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_torneos_ligas",
                        column: x => x.tenant_id,
                        principalSchema: "goalflow",
                        principalTable: "ligas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "equipos_torneo",
                schema: "goalflow",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    torneo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    equipo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    estado = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_equipos_torneo", x => x.id);
                    table.UniqueConstraint("ak_equipos_torneo_tenant_torneo_id", x => new { x.tenant_id, x.torneo_id, x.id });
                    table.CheckConstraint("ck_equipos_torneo_estado", "estado in ('Inscrito', 'Retirado')");
                    table.ForeignKey(
                        name: "fk_equipos_torneo_equipos",
                        columns: x => new { x.tenant_id, x.equipo_id },
                        principalSchema: "goalflow",
                        principalTable: "equipos",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_equipos_torneo_torneos",
                        columns: x => new { x.tenant_id, x.torneo_id },
                        principalSchema: "goalflow",
                        principalTable: "torneos",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fases",
                schema: "goalflow",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    torneo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    orden = table.Column<int>(type: "integer", nullable: false),
                    tipo = table.Column<string>(type: "text", nullable: false),
                    vueltas = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fases", x => x.id);
                    table.UniqueConstraint("ak_fases_tenant_torneo_id", x => new { x.tenant_id, x.torneo_id, x.id });
                    table.CheckConstraint("ck_fases_orden", "orden >= 1");
                    table.CheckConstraint("ck_fases_tipo", "tipo in ('TodosContraTodos')");
                    table.CheckConstraint("ck_fases_vueltas", "vueltas in (1, 2)");
                    table.ForeignKey(
                        name: "fk_fases_torneos",
                        columns: x => new { x.tenant_id, x.torneo_id },
                        principalSchema: "goalflow",
                        principalTable: "torneos",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inscripciones_jugador",
                schema: "goalflow",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    torneo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    equipo_torneo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    jugador_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inscripciones_jugador", x => x.id);
                    table.ForeignKey(
                        name: "fk_inscripciones_jugador_equipos_torneo",
                        columns: x => new { x.tenant_id, x.torneo_id, x.equipo_torneo_id },
                        principalSchema: "goalflow",
                        principalTable: "equipos_torneo",
                        principalColumns: new[] { "tenant_id", "torneo_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inscripciones_jugador_jugadores",
                        columns: x => new { x.tenant_id, x.jugador_id },
                        principalSchema: "goalflow",
                        principalTable: "jugadores",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "partidos",
                schema: "goalflow",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    torneo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fase_id = table.Column<Guid>(type: "uuid", nullable: false),
                    jornada = table.Column<int>(type: "integer", nullable: false),
                    local_id = table.Column<Guid>(type: "uuid", nullable: false),
                    visitante_id = table.Column<Guid>(type: "uuid", nullable: false),
                    programado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    estado = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_partidos", x => x.id);
                    table.CheckConstraint("ck_partidos_estado", "estado in ('Programado', 'Cancelado')");
                    table.CheckConstraint("ck_partidos_jornada", "jornada >= 1");
                    table.CheckConstraint("ck_partidos_local_distinto_visitante", "local_id <> visitante_id");
                    table.ForeignKey(
                        name: "fk_partidos_fases",
                        columns: x => new { x.tenant_id, x.torneo_id, x.fase_id },
                        principalSchema: "goalflow",
                        principalTable: "fases",
                        principalColumns: new[] { "tenant_id", "torneo_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_partidos_local",
                        columns: x => new { x.tenant_id, x.torneo_id, x.local_id },
                        principalSchema: "goalflow",
                        principalTable: "equipos_torneo",
                        principalColumns: new[] { "tenant_id", "torneo_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_partidos_visitante",
                        columns: x => new { x.tenant_id, x.torneo_id, x.visitante_id },
                        principalSchema: "goalflow",
                        principalTable: "equipos_torneo",
                        principalColumns: new[] { "tenant_id", "torneo_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_equipos_tenant_codigo",
                schema: "goalflow",
                table: "equipos",
                columns: new[] { "tenant_id", "codigo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_equipos_torneo_tenant_id_equipo_id",
                schema: "goalflow",
                table: "equipos_torneo",
                columns: new[] { "tenant_id", "equipo_id" });

            migrationBuilder.CreateIndex(
                name: "ux_equipos_torneo_tenant_torneo_equipo",
                schema: "goalflow",
                table: "equipos_torneo",
                columns: new[] { "tenant_id", "torneo_id", "equipo_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_fases_tenant_torneo_orden",
                schema: "goalflow",
                table: "fases",
                columns: new[] { "tenant_id", "torneo_id", "orden" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inscripciones_jugador_tenant_id_jugador_id",
                schema: "goalflow",
                table: "inscripciones_jugador",
                columns: new[] { "tenant_id", "jugador_id" });

            migrationBuilder.CreateIndex(
                name: "IX_inscripciones_jugador_tenant_id_torneo_id_equipo_torneo_id",
                schema: "goalflow",
                table: "inscripciones_jugador",
                columns: new[] { "tenant_id", "torneo_id", "equipo_torneo_id" });

            migrationBuilder.CreateIndex(
                name: "ux_inscripciones_jugador_tenant_torneo_jugador",
                schema: "goalflow",
                table: "inscripciones_jugador",
                columns: new[] { "tenant_id", "torneo_id", "jugador_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_jugadores_tenant_codigo",
                schema: "goalflow",
                table: "jugadores",
                columns: new[] { "tenant_id", "codigo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_partidos_tenant_id_torneo_id_fase_id",
                schema: "goalflow",
                table: "partidos",
                columns: new[] { "tenant_id", "torneo_id", "fase_id" });

            migrationBuilder.CreateIndex(
                name: "IX_partidos_tenant_id_torneo_id_local_id",
                schema: "goalflow",
                table: "partidos",
                columns: new[] { "tenant_id", "torneo_id", "local_id" });

            migrationBuilder.CreateIndex(
                name: "IX_partidos_tenant_id_torneo_id_visitante_id",
                schema: "goalflow",
                table: "partidos",
                columns: new[] { "tenant_id", "torneo_id", "visitante_id" });

            migrationBuilder.CreateIndex(
                name: "ux_partidos_tenant_fase_local_visitante",
                schema: "goalflow",
                table: "partidos",
                columns: new[] { "tenant_id", "fase_id", "local_id", "visitante_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_torneos_tenant_codigo",
                schema: "goalflow",
                table: "torneos",
                columns: new[] { "tenant_id", "codigo" },
                unique: true);

            // RLS en la misma migración que cada tabla (ADR-14 §2.2); la política es la de equipos y entregas_eventos.
            foreach (var tabla in new[] { "torneos", "fases", "jugadores", "equipos_torneo", "inscripciones_jugador", "partidos" })
            {
                migrationBuilder.Sql($"""
                    ALTER TABLE goalflow.{tabla} ENABLE ROW LEVEL SECURITY;
                    CREATE POLICY {tabla}_tenant ON goalflow.{tabla}
                        FOR ALL
                        USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                        WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                    """);
            }

            // Los default privileges de db/roles/001_roles.sql conceden DELETE sobre toda tabla nueva (Spec 005 R-10).
            migrationBuilder.Sql("""
                REVOKE DELETE ON goalflow.torneos, goalflow.fases, goalflow.jugadores, goalflow.equipos_torneo,
                    goalflow.inscripciones_jugador, goalflow.partidos, goalflow.equipos FROM goalflow_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inscripciones_jugador",
                schema: "goalflow");

            migrationBuilder.DropTable(
                name: "partidos",
                schema: "goalflow");

            migrationBuilder.DropTable(
                name: "jugadores",
                schema: "goalflow");

            migrationBuilder.DropTable(
                name: "fases",
                schema: "goalflow");

            migrationBuilder.DropTable(
                name: "equipos_torneo",
                schema: "goalflow");

            migrationBuilder.DropTable(
                name: "torneos",
                schema: "goalflow");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_equipos_tenant_id_id",
                schema: "goalflow",
                table: "equipos");

            migrationBuilder.DropIndex(
                name: "ux_equipos_tenant_codigo",
                schema: "goalflow",
                table: "equipos");

            migrationBuilder.DropColumn(
                name: "codigo",
                schema: "goalflow",
                table: "equipos");
        }
    }
}
