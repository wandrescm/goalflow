using Npgsql;
using static GoalFlow.Tests.Isolation.BaseDeAislamiento;

namespace GoalFlow.Tests.Isolation;

/// <summary>
/// Spec 005 T-15, T-16 y T-17: SQL crudo como goalflow_app contra la base desechable (lista permitida de ADR-14 C-10).
/// Toda sentencia corre en una transacción que se revierte.
/// </summary>
[Collection(ColeccionDeAislamiento.Nombre)]
public class ClavesDeTorneosTests(BaseDeAislamiento db)
{
    private const string ViolaClaveForanea = "23503";
    private const string ViolaUnicidad = "23505";
    private const string ViolaCheck = "23514";
    private const string PermisoDenegado = "42501";

    private static readonly string[] TablasSinDelete =
        ["equipos", "torneos", "fases", "equipos_torneo", "jugadores", "inscripciones_jugador", "partidos"];

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

    private static string Partido(Guid tenant, Guid torneo, Guid fase, Guid local, Guid visitante) => $"""
        insert into goalflow.partidos (id, tenant_id, torneo_id, fase_id, jornada, local_id, visitante_id, estado)
        values ('{Guid.NewGuid()}', '{tenant}', '{torneo}', '{fase}', 1, '{local}', '{visitante}', 'Programado')
        """;

    private static string Inscripcion(Guid tenant, Guid torneo, Guid participacion, Guid jugador) => $"""
        insert into goalflow.inscripciones_jugador (id, tenant_id, torneo_id, equipo_torneo_id, jugador_id)
        values ('{Guid.NewGuid()}', '{tenant}', '{torneo}', '{participacion}', '{jugador}')
        """;

    // ---- T-15 (R-27): una fila de A no puede referenciar una fila de B ----

    [Fact]
    public async Task T15_un_partido_de_A_que_referencia_una_participacion_de_B_es_rechazado_con_23503()
    {
        var g = await GrafoDeTorneos.ObtenerAsync(db);

        var error = await RechazadoParaLigaA(Partido(LigaA.Value, g.TorneoA, g.FaseA, g.ParticipacionA1, g.ParticipacionB1));

        Assert.Equal(ViolaClaveForanea, error.SqlState);
    }

    [Fact]
    public async Task T15_una_inscripcion_de_A_que_referencia_un_jugador_de_B_es_rechazada_con_23503()
    {
        var g = await GrafoDeTorneos.ObtenerAsync(db);

        var error = await RechazadoParaLigaA(Inscripcion(LigaA.Value, g.TorneoA, g.ParticipacionA1, g.JugadorB));

        Assert.Equal(ViolaClaveForanea, error.SqlState);
    }

    [Fact]
    public async Task T15_los_mismos_inserts_con_identificadores_aleatorios_tambien_dan_23503()
    {
        var g = await GrafoDeTorneos.ObtenerAsync(db);

        var partido = await RechazadoParaLigaA(
            Partido(LigaA.Value, g.TorneoA, g.FaseA, Guid.NewGuid(), Guid.NewGuid()));
        var inscripcion = await RechazadoParaLigaA(
            Inscripcion(LigaA.Value, g.TorneoA, g.ParticipacionA1, Guid.NewGuid()));

        Assert.Equal(ViolaClaveForanea, partido.SqlState);
        Assert.Equal(ViolaClaveForanea, inscripcion.SqlState);
    }

    /// <summary>
    /// Canario: una FK simple a equipos(id) sí acepta la referencia a un equipo de B, porque las comprobaciones de
    /// clave foránea no pasan por RLS. Si este canario fallara, T-15 no vería la amenaza. Todo en una transacción
    /// de superusuario que se revierte; solo la inserción corre con el rol goalflow_app.
    /// </summary>
    [Fact]
    public async Task T15_canario_una_FK_simple_acepta_la_referencia_a_un_equipo_de_otra_liga()
    {
        var g = await GrafoDeTorneos.ObtenerAsync(db);
        await using var conexion = new NpgsqlConnection(db.CadenaSuperusuario);
        await conexion.OpenAsync();
        await using var transaccion = await conexion.BeginTransactionAsync();

        await Ejecutar(conexion, transaccion, "create table goalflow.canario_fk_simple (equipo_id uuid references goalflow.equipos (id))");
        await Ejecutar(conexion, transaccion, "grant all on goalflow.canario_fk_simple to goalflow_app");
        await Ejecutar(conexion, transaccion, "set local role goalflow_app");
        await Ejecutar(conexion, transaccion, $"select set_config('app.tenant_id', '{LigaA.Value}', true)");

        // Debe pasar: la FK simple no distingue ligas.
        await Ejecutar(conexion, transaccion, $"insert into goalflow.canario_fk_simple (equipo_id) values ('{g.EquipoB}')");

        await transaccion.RollbackAsync();
    }

    /// <summary>
    /// Un caso por cada clave foránea compuesta entre tablas con tenant de la migración (Spec 005 T-15, C-22): solo la
    /// columna de esa clave apunta a una fila de B; las demás apuntan a filas válidas de A. Se afirma también el nombre
    /// de la restricción que dispara, para que una clave hermana no oculte la que falta.
    /// </summary>
    [Theory]
    [InlineData("fases → torneos", "fk_fases_torneos")]
    [InlineData("equipos_torneo → torneos", "fk_equipos_torneo_torneos")]
    [InlineData("equipos_torneo → equipos", "fk_equipos_torneo_equipos")]
    [InlineData("inscripciones_jugador → equipos_torneo", "fk_inscripciones_jugador_equipos_torneo")]
    [InlineData("inscripciones_jugador → jugadores", "fk_inscripciones_jugador_jugadores")]
    [InlineData("partidos → fases", "fk_partidos_fases")]
    [InlineData("partidos → equipos_torneo (local)", "fk_partidos_local")]
    [InlineData("partidos → equipos_torneo (visitante)", "fk_partidos_visitante")]
    public async Task T15_cada_clave_foranea_compuesta_rechaza_una_referencia_a_B_con_23503(string clave, string restriccion)
    {
        var g = await GrafoDeTorneos.ObtenerAsync(db);
        var a = LigaA.Value;
        var sql = clave switch
        {
            "fases → torneos" => $"""
                insert into goalflow.fases (id, tenant_id, torneo_id, orden, tipo, vueltas)
                values ('{Guid.NewGuid()}', '{a}', '{g.TorneoB}', 98, 'TodosContraTodos', 1)
                """,
            "equipos_torneo → torneos" => $"""
                insert into goalflow.equipos_torneo (id, tenant_id, torneo_id, equipo_id, estado)
                values ('{Guid.NewGuid()}', '{a}', '{g.TorneoB}', '{g.EquipoA}', 'Inscrito')
                """,
            "equipos_torneo → equipos" => $"""
                insert into goalflow.equipos_torneo (id, tenant_id, torneo_id, equipo_id, estado)
                values ('{Guid.NewGuid()}', '{a}', '{g.TorneoA}', '{g.EquipoB}', 'Inscrito')
                """,
            "inscripciones_jugador → equipos_torneo" => Inscripcion(a, g.TorneoA, g.ParticipacionB1, g.JugadorA),
            "inscripciones_jugador → jugadores" => Inscripcion(a, g.TorneoA, g.ParticipacionA1, g.JugadorB),
            "partidos → fases" => Partido(a, g.TorneoA, g.FaseB, g.ParticipacionA1, g.ParticipacionA2),
            "partidos → equipos_torneo (local)" => Partido(a, g.TorneoA, g.FaseA, g.ParticipacionB1, g.ParticipacionA2),
            "partidos → equipos_torneo (visitante)" => Partido(a, g.TorneoA, g.FaseA, g.ParticipacionA1, g.ParticipacionB1),
            _ => throw new InvalidOperationException(clave),
        };

        var error = await RechazadoParaLigaA(sql);

        Assert.Equal(ViolaClaveForanea, error.SqlState);
        Assert.Equal(restriccion, error.ConstraintName);
    }

    // ---- T-16 (R-07, R-05, R-06): la FK incluye torneo_id; unicidad y checks ----

    [Fact]
    public async Task T16_un_partido_cuya_fase_es_de_otro_torneo_de_A_es_rechazado_con_23503()
    {
        var g = await GrafoDeTorneos.ObtenerAsync(db);

        var error = await RechazadoParaLigaA(Partido(LigaA.Value, g.TorneoA, g.FaseA2, g.ParticipacionA1, g.ParticipacionA2));

        Assert.Equal(ViolaClaveForanea, error.SqlState);
    }

    [Fact]
    public async Task T16_un_partido_cuya_participacion_es_de_otro_torneo_de_A_es_rechazado_con_23503()
    {
        var g = await GrafoDeTorneos.ObtenerAsync(db);

        var error = await RechazadoParaLigaA(
            Partido(LigaA.Value, g.TorneoA, g.FaseA, g.ParticipacionA1, g.ParticipacionA2DelOtroTorneo));

        Assert.Equal(ViolaClaveForanea, error.SqlState);
    }

    [Fact]
    public async Task T16_una_segunda_inscripcion_del_mismo_jugador_en_el_mismo_torneo_es_rechazada_con_23505()
    {
        var g = await GrafoDeTorneos.ObtenerAsync(db);
        var (conexion, transaccion) = await TransaccionDeLaLigaA();
        await using var _ = conexion;
        await using var __ = transaccion;

        await Ejecutar(conexion, transaccion, Inscripcion(LigaA.Value, g.TorneoA, g.ParticipacionA1, g.JugadorA));
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            Ejecutar(conexion, transaccion, Inscripcion(LigaA.Value, g.TorneoA, g.ParticipacionA2, g.JugadorA)));

        Assert.Equal(ViolaUnicidad, error.SqlState);
    }

    [Fact]
    public async Task T16_un_partido_con_local_igual_a_visitante_es_rechazado_con_23514()
    {
        var g = await GrafoDeTorneos.ObtenerAsync(db);

        var error = await RechazadoParaLigaA(Partido(LigaA.Value, g.TorneoA, g.FaseA, g.ParticipacionA1, g.ParticipacionA1));

        Assert.Equal(ViolaCheck, error.SqlState);
    }

    // ---- T-17 (R-10): DELETE revocado ----

    [Theory]
    [MemberData(nameof(Tablas))]
    public async Task T17_DELETE_sobre_las_tablas_nuevas_y_equipos_es_rechazado_por_permiso(string tabla)
    {
        var error = await RechazadoParaLigaA($"delete from goalflow.{tabla}");

        Assert.Equal(PermisoDenegado, error.SqlState);
        Assert.StartsWith("permission denied for table", error.MessageText, StringComparison.Ordinal);
    }

    public static TheoryData<string> Tablas()
    {
        var datos = new TheoryData<string>();
        foreach (var tabla in TablasSinDelete) datos.Add(tabla);
        return datos;
    }
}
