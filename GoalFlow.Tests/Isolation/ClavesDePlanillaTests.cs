using Npgsql;
using static GoalFlow.Tests.Isolation.BaseDeAislamiento;

namespace GoalFlow.Tests.Isolation;

/// <summary>
/// Filas de la planilla de las ligas A y B, creadas una sola vez como superusuario (preparación). Es un grafo propio, con
/// torneos, equipos y jugadores que ninguna otra prueba usa: así no choca con las inserciones de Spec 005 T-15 a T-17.
/// Datos inventados (Spec 006 §4).
/// </summary>
public sealed record GrafoDePlanillas(
    Guid TorneoA, Guid ParticipacionA1, Guid ParticipacionA2, Guid PartidoA, Guid PlanillaA, Guid InscripcionA1,
    Guid TorneoA2, Guid InscripcionDelOtroTorneo,
    Guid TorneoB, Guid ParticipacionB1, Guid PartidoB, Guid PlanillaB, Guid InscripcionB1)
{
    private static readonly SemaphoreSlim Candado = new(1, 1);
    private static readonly Dictionary<string, GrafoDePlanillas> PorBase = [];

    public static async Task<GrafoDePlanillas> ObtenerAsync(BaseDeAislamiento db)
    {
        await Candado.WaitAsync();
        try
        {
            if (PorBase.TryGetValue(db.CadenaSuperusuario, out var existente)) return existente;

            var g = new GrafoDePlanillas(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                Guid.NewGuid(), Guid.NewGuid(),
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            var (a, b) = (LigaA.Value, LigaB.Value);
            var s = Guid.NewGuid().ToString("N")[..8];
            var (faseA, faseA2, faseB) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            var (equipoA1, equipoA2, equipoB1, equipoB2) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            var (participacionB2, participacionDelOtroTorneo) = (Guid.NewGuid(), Guid.NewGuid());
            var (jugadorA1, jugadorOtroTorneo, jugadorB1) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

            await db.EjecutarComoSuperusuario($"""
                insert into goalflow.torneos (id, tenant_id, codigo, nombre) values
                  ('{g.TorneoA}', '{a}', 'PL-A1-{s}', 'Torneo Planilla A'),
                  ('{g.TorneoA2}', '{a}', 'PL-A2-{s}', 'Torneo Planilla Dos A'),
                  ('{g.TorneoB}', '{b}', 'PL-B1-{s}', 'Torneo Planilla B');
                insert into goalflow.fases (id, tenant_id, torneo_id, orden, tipo, vueltas) values
                  ('{faseA}', '{a}', '{g.TorneoA}', 1, 'TodosContraTodos', 1),
                  ('{faseA2}', '{a}', '{g.TorneoA2}', 1, 'TodosContraTodos', 1),
                  ('{faseB}', '{b}', '{g.TorneoB}', 1, 'TodosContraTodos', 1);
                insert into goalflow.equipos (id, tenant_id, codigo, nombre) values
                  ('{equipoA1}', '{a}', 'PL-EA1-{s}', 'Equipo Planilla A1'),
                  ('{equipoA2}', '{a}', 'PL-EA2-{s}', 'Equipo Planilla A2'),
                  ('{equipoB1}', '{b}', 'PL-EB1-{s}', 'Equipo Planilla B1'),
                  ('{equipoB2}', '{b}', 'PL-EB2-{s}', 'Equipo Planilla B2');
                insert into goalflow.equipos_torneo (id, tenant_id, torneo_id, equipo_id, estado) values
                  ('{g.ParticipacionA1}', '{a}', '{g.TorneoA}', '{equipoA1}', 'Inscrito'),
                  ('{g.ParticipacionA2}', '{a}', '{g.TorneoA}', '{equipoA2}', 'Inscrito'),
                  ('{participacionDelOtroTorneo}', '{a}', '{g.TorneoA2}', '{equipoA1}', 'Inscrito'),
                  ('{g.ParticipacionB1}', '{b}', '{g.TorneoB}', '{equipoB1}', 'Inscrito'),
                  ('{participacionB2}', '{b}', '{g.TorneoB}', '{equipoB2}', 'Inscrito');
                insert into goalflow.jugadores (id, tenant_id, codigo, nombre) values
                  ('{jugadorA1}', '{a}', 'PL-JA1-{s}', 'Jugador Planilla A1'),
                  ('{jugadorOtroTorneo}', '{a}', 'PL-JA2-{s}', 'Jugador Planilla A2'),
                  ('{jugadorB1}', '{b}', 'PL-JB1-{s}', 'Jugador Planilla B1');
                insert into goalflow.inscripciones_jugador (id, tenant_id, torneo_id, equipo_torneo_id, jugador_id) values
                  ('{g.InscripcionA1}', '{a}', '{g.TorneoA}', '{g.ParticipacionA1}', '{jugadorA1}'),
                  ('{g.InscripcionDelOtroTorneo}', '{a}', '{g.TorneoA2}', '{participacionDelOtroTorneo}', '{jugadorOtroTorneo}'),
                  ('{g.InscripcionB1}', '{b}', '{g.TorneoB}', '{g.ParticipacionB1}', '{jugadorB1}');
                insert into goalflow.partidos (id, tenant_id, torneo_id, fase_id, jornada, local_id, visitante_id, estado) values
                  ('{g.PartidoA}', '{a}', '{g.TorneoA}', '{faseA}', 1, '{g.ParticipacionA1}', '{g.ParticipacionA2}', 'Programado'),
                  ('{g.PartidoB}', '{b}', '{g.TorneoB}', '{faseB}', 1, '{g.ParticipacionB1}', '{participacionB2}', 'Programado');
                insert into goalflow.planillas (id, tenant_id, torneo_id, partido_id, version, estado, guardado_por) values
                  ('{g.PlanillaA}', '{a}', '{g.TorneoA}', '{g.PartidoA}', 1, 'Programado', '{UsuarioU1}'),
                  ('{g.PlanillaB}', '{b}', '{g.TorneoB}', '{g.PartidoB}', 1, 'Programado', '{UsuarioU2}');
                """);

            PorBase[db.CadenaSuperusuario] = g;
            return g;
        }
        finally
        {
            Candado.Release();
        }
    }
}

/// <summary>
/// Spec 006 T-14 y T-15: SQL crudo como goalflow_app contra la base desechable (lista permitida de ADR-14 C-10).
/// Toda sentencia corre en una transacción que se revierte.
/// </summary>
[Collection(ColeccionDeAislamiento.Nombre)]
public class ClavesDePlanillaTests(BaseDeAislamiento db)
{
    private const string ViolaClaveForanea = "23503";
    private const string ViolaUnicidad = "23505";
    private const string ViolaCheck = "23514";
    private const string PermisoDenegado = "42501";

    private static readonly string[] TablasDeLaPlanilla = ["planillas", "goles", "tarjetas"];

    private static async Task Ejecutar(NpgsqlConnection conexion, NpgsqlTransaction transaccion, string sql)
    {
        await using var comando = new NpgsqlCommand(sql, conexion, transaccion);
        await comando.ExecuteNonQueryAsync();
    }

    private async Task<(NpgsqlConnection Conexion, NpgsqlTransaction Transaccion)> TransaccionDeLaLigaA()
    {
        var conexion = new NpgsqlConnection(db.CadenaApp);
        await conexion.OpenAsync();
        var transaccion = await conexion.BeginTransactionAsync();
        await Ejecutar(conexion, transaccion, $"select set_config('app.tenant_id', '{LigaA.Value}', true)");
        return (conexion, transaccion);
    }

    private async Task<PostgresException> RechazadoParaLigaA(string sql)
    {
        var (conexion, transaccion) = await TransaccionDeLaLigaA();
        await using var _ = conexion;
        await using var __ = transaccion;
        return await Assert.ThrowsAsync<PostgresException>(() => Ejecutar(conexion, transaccion, sql));
    }

    private static string Planilla(Guid tenant, Guid torneo, Guid partido, int version = 2) => $"""
        insert into goalflow.planillas (id, tenant_id, torneo_id, partido_id, version, estado, guardado_por)
        values ('{Guid.NewGuid()}', '{tenant}', '{torneo}', '{partido}', {version}, 'Programado', '{UsuarioU1}')
        """;

    private static string Gol(
        Guid tenant, Guid torneo, Guid planilla, Guid participacion, Guid? inscripcion, string tipo = "Jugador", int cantidad = 1) => $"""
        insert into goalflow.goles (id, tenant_id, torneo_id, planilla_id, tipo, equipo_torneo_id, inscripcion_id, cantidad)
        values ('{Guid.NewGuid()}', '{tenant}', '{torneo}', '{planilla}', '{tipo}', '{participacion}', {Nulo(inscripcion)}, {cantidad})
        """;

    private static string Tarjeta(
        Guid tenant, Guid torneo, Guid planilla, Guid inscripcion, string color = "Amarilla", int cantidad = 1) => $"""
        insert into goalflow.tarjetas (id, tenant_id, torneo_id, planilla_id, inscripcion_id, color, cantidad)
        values ('{Guid.NewGuid()}', '{tenant}', '{torneo}', '{planilla}', '{inscripcion}', '{color}', {cantidad})
        """;

    private static string Nulo(Guid? valor) => valor is { } v ? $"'{v}'" : "null";

    // ---- T-14 (R-05): una fila de A no puede referenciar una fila de B, una prueba por clave ----

    [Theory]
    [InlineData("planillas → partidos", "fk_planillas_partidos")]
    [InlineData("goles → planillas", "fk_goles_planillas")]
    [InlineData("goles → equipos_torneo", "fk_goles_equipos_torneo")]
    [InlineData("goles → inscripciones_jugador", "fk_goles_inscripciones_jugador")]
    [InlineData("tarjetas → planillas", "fk_tarjetas_planillas")]
    [InlineData("tarjetas → inscripciones_jugador", "fk_tarjetas_inscripciones_jugador")]
    public async Task T14_cada_clave_foranea_nueva_rechaza_una_referencia_a_B_con_23503(string clave, string restriccion)
    {
        var g = await GrafoDePlanillas.ObtenerAsync(db);
        var a = LigaA.Value;
        var sql = clave switch
        {
            "planillas → partidos" => Planilla(a, g.TorneoA, g.PartidoB),
            "goles → planillas" => Gol(a, g.TorneoA, g.PlanillaB, g.ParticipacionA1, g.InscripcionA1),
            "goles → equipos_torneo" => Gol(a, g.TorneoA, g.PlanillaA, g.ParticipacionB1, g.InscripcionA1),
            "goles → inscripciones_jugador" => Gol(a, g.TorneoA, g.PlanillaA, g.ParticipacionA1, g.InscripcionB1),
            "tarjetas → planillas" => Tarjeta(a, g.TorneoA, g.PlanillaB, g.InscripcionA1),
            "tarjetas → inscripciones_jugador" => Tarjeta(a, g.TorneoA, g.PlanillaA, g.InscripcionB1),
            _ => throw new InvalidOperationException(clave),
        };

        var error = await RechazadoParaLigaA(sql);

        Assert.Equal(ViolaClaveForanea, error.SqlState);
        Assert.Equal(restriccion, error.ConstraintName);
    }

    [Fact]
    public async Task T14_los_mismos_inserts_con_identificadores_aleatorios_tambien_dan_23503()
    {
        var g = await GrafoDePlanillas.ObtenerAsync(db);
        var a = LigaA.Value;

        var sentencias = new[]
        {
            Planilla(a, g.TorneoA, Guid.NewGuid()),
            Gol(a, g.TorneoA, Guid.NewGuid(), g.ParticipacionA1, g.InscripcionA1),
            Gol(a, g.TorneoA, g.PlanillaA, Guid.NewGuid(), g.InscripcionA1),
            Gol(a, g.TorneoA, g.PlanillaA, g.ParticipacionA1, Guid.NewGuid()),
            Tarjeta(a, g.TorneoA, Guid.NewGuid(), g.InscripcionA1),
            Tarjeta(a, g.TorneoA, g.PlanillaA, Guid.NewGuid()),
        };

        foreach (var sql in sentencias)
            Assert.Equal(ViolaClaveForanea, (await RechazadoParaLigaA(sql)).SqlState);
    }

    [Fact]
    public async Task T14_un_gol_cuya_inscripcion_es_de_otro_torneo_de_A_es_rechazado_con_23503()
    {
        var g = await GrafoDePlanillas.ObtenerAsync(db);

        var error = await RechazadoParaLigaA(
            Gol(LigaA.Value, g.TorneoA, g.PlanillaA, g.ParticipacionA1, g.InscripcionDelOtroTorneo));

        Assert.Equal(ViolaClaveForanea, error.SqlState);
        Assert.Equal("fk_goles_inscripciones_jugador", error.ConstraintName);
    }

    [Fact]
    public async Task T14_una_segunda_fila_SinAutor_de_la_misma_planilla_y_equipo_acreditado_es_rechazada_con_23505()
    {
        var g = await GrafoDePlanillas.ObtenerAsync(db);
        var (conexion, transaccion) = await TransaccionDeLaLigaA();
        await using var _ = conexion;
        await using var __ = transaccion;

        await Ejecutar(conexion, transaccion, Gol(LigaA.Value, g.TorneoA, g.PlanillaA, g.ParticipacionA1, null, "SinAutor"));
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            Ejecutar(conexion, transaccion, Gol(LigaA.Value, g.TorneoA, g.PlanillaA, g.ParticipacionA1, null, "SinAutor", 2)));

        Assert.Equal(ViolaUnicidad, error.SqlState);
    }

    [Fact]
    public async Task T14_una_segunda_version_igual_del_mismo_partido_es_rechazada_con_23505()
    {
        var g = await GrafoDePlanillas.ObtenerAsync(db);

        var error = await RechazadoParaLigaA(Planilla(LigaA.Value, g.TorneoA, g.PartidoA, version: 1));

        Assert.Equal(ViolaUnicidad, error.SqlState);
        Assert.Equal("ux_planillas_tenant_partido_version", error.ConstraintName);
    }

    [Fact]
    public async Task T14_un_gol_SinAutor_con_inscripcion_es_rechazado_con_23514()
    {
        var g = await GrafoDePlanillas.ObtenerAsync(db);

        var error = await RechazadoParaLigaA(
            Gol(LigaA.Value, g.TorneoA, g.PlanillaA, g.ParticipacionA1, g.InscripcionA1, "SinAutor"));

        Assert.Equal(ViolaCheck, error.SqlState);
    }

    [Fact]
    public async Task T14_un_gol_de_jugador_sin_inscripcion_es_rechazado_con_23514()
    {
        var g = await GrafoDePlanillas.ObtenerAsync(db);

        var error = await RechazadoParaLigaA(
            Gol(LigaA.Value, g.TorneoA, g.PlanillaA, g.ParticipacionA1, null, "Jugador"));

        Assert.Equal(ViolaCheck, error.SqlState);
    }

    [Fact]
    public async Task T14_una_tarjeta_de_cantidad_10_es_rechazada_con_23514()
    {
        var g = await GrafoDePlanillas.ObtenerAsync(db);

        var error = await RechazadoParaLigaA(Tarjeta(LigaA.Value, g.TorneoA, g.PlanillaA, g.InscripcionA1, cantidad: 10));

        Assert.Equal(ViolaCheck, error.SqlState);
    }

    [Fact]
    public async Task T14_un_estado_de_partido_fuera_de_los_cinco_es_rechazado_con_23514()
    {
        var g = await GrafoDePlanillas.ObtenerAsync(db);

        var error = await RechazadoParaLigaA($"update goalflow.partidos set estado = 'Perdido' where id = '{g.PartidoA}'");

        Assert.Equal(ViolaCheck, error.SqlState);
        Assert.Equal("ck_partidos_estado", error.ConstraintName);
    }

    [Theory]
    [InlineData("Jugado")]
    [InlineData("Suspendido")]
    [InlineData("Adjudicado")]
    public async Task T14_los_cinco_estados_de_R_01_son_aceptados(string estado)
    {
        var g = await GrafoDePlanillas.ObtenerAsync(db);
        var (conexion, transaccion) = await TransaccionDeLaLigaA();
        await using var _ = conexion;
        await using var __ = transaccion;

        await Ejecutar(conexion, transaccion, $"update goalflow.partidos set estado = '{estado}' where id = '{g.PartidoA}'");
    }

    // ---- T-15 (R-06, R-21): la base impide reescribir el historial ----

    [Theory]
    [InlineData("planillas", "update")]
    [InlineData("goles", "update")]
    [InlineData("tarjetas", "update")]
    [InlineData("planillas", "delete")]
    [InlineData("goles", "delete")]
    [InlineData("tarjetas", "delete")]
    public async Task T15_UPDATE_y_DELETE_sobre_las_tablas_de_la_planilla_son_rechazados_por_permiso(string tabla, string operacion)
    {
        var columna = tabla == "planillas" ? "version" : "cantidad";
        var sql = operacion == "update"
            ? $"update goalflow.{tabla} set {columna} = {columna}"
            : $"delete from goalflow.{tabla}";

        var error = await RechazadoParaLigaA(sql);

        Assert.Equal(PermisoDenegado, error.SqlState);
        Assert.StartsWith("permission denied for table", error.MessageText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task T15_INSERT_funciona_dentro_de_una_transaccion_del_tenant()
    {
        var g = await GrafoDePlanillas.ObtenerAsync(db);
        var (conexion, transaccion) = await TransaccionDeLaLigaA();
        await using var _ = conexion;
        await using var __ = transaccion;

        await Ejecutar(conexion, transaccion, Planilla(LigaA.Value, g.TorneoA, g.PartidoA, version: 2));
        await Ejecutar(conexion, transaccion, Gol(LigaA.Value, g.TorneoA, g.PlanillaA, g.ParticipacionA1, g.InscripcionA1));
        await Ejecutar(conexion, transaccion, Tarjeta(LigaA.Value, g.TorneoA, g.PlanillaA, g.InscripcionA1, "Azul", 2));
    }

    [Fact]
    public async Task T15_el_catalogo_de_las_tres_tablas_existe()
    {
        await using var conexion = new NpgsqlConnection(db.CadenaSuperusuario);
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand(
            "select count(*) from pg_class c join pg_namespace n on n.oid = c.relnamespace where n.nspname = 'goalflow' and c.relname = any(@t)",
            conexion);
        comando.Parameters.AddWithValue("t", TablasDeLaPlanilla);

        Assert.Equal(3L, (long)(await comando.ExecuteScalarAsync())!);
    }
}
