using GoalFlow.Domain.Torneos;

namespace GoalFlow.Tests.Torneos;

/// <summary>Spec 005 T-02, T-03 y T-32: el validador puro de fixtures (R-13, R-39).</summary>
public class ValidadorDeFixtureTests
{
    private static JugadorDescripcion J(string codigo) => new(codigo, $"Jugador {codigo}");

    private static EquipoDescripcion E(string codigo, bool retirado = false, params string[] jugadores) =>
        new(codigo, $"Equipo {codigo}", retirado, [.. jugadores.Select(J)]);

    private static FaseDescripcion F(int orden = 1, int vueltas = 1) => new(orden, nameof(TipoDeFase.TodosContraTodos), vueltas);

    private static PartidoDescripcion P(string local, string visitante, int jornada, int fase = 1, bool cancelado = false) =>
        new(fase, jornada, local, visitante, null, cancelado);

    private static FixtureDescripcion Fixture(
        IEnumerable<FaseDescripcion> fases, IEnumerable<EquipoDescripcion> equipos, IEnumerable<PartidoDescripcion> partidos) =>
        new("Torneo Uno", [.. fases], [.. equipos], [.. partidos]);

    private static EquipoDescripcion[] CuatroEquipos() => [E("A"), E("B"), E("C"), E("D")];

    private static void SinErrores(FixtureDescripcion fixture) =>
        Assert.Empty(ValidadorDeFixture.Validar(fixture));

    private static ErrorDeFixture Unico(FixtureDescripcion fixture) =>
        Assert.Single(ValidadorDeFixture.Validar(fixture));

    [Fact] // T-02
    public void Un_fixture_valido_no_devuelve_errores() =>
        SinErrores(Fixture(
            [F(1, 2), F(2)],
            [E("A", false, "a1"), E("B", false, "b1"), E("C", false, "c1"), E("D", true, "d1")],
            [P("A", "B", 1), P("C", "D", 1, cancelado: true), P("B", "A", 2), P("A", "C", 3, fase: 2)]));

    [Fact] // T-02
    public void Un_payload_con_una_violacion_de_cada_regla_devuelve_exactamente_siete_errores_con_su_ruta()
    {
        var fixture = Fixture(
            [F(1), F(1)],                                                    // (c) fases[1].orden
            [
                E("A", false, "pA1"),
                E("B", false, "pB1", "pA1"),                                 // (b) equipos[1].jugadores[1].codigo
                E("C"),
                E("D", true),
                E("A"),                                                      // (a) equipos[4].codigo
                E("F"),
            ],
            [
                P("A", "B", 1),
                P("A", "B", 2),                                              // (e) partidos[1]
                P("B", "C", 3),
                P("B", "F", 3),                                              // (f) partidos[3]
                P("C", "D", 4),                                              // (h) partidos[4].cancelado
                P("A", "C", 1, fase: 9),                                     // (d) partidos[5].fase
            ]);

        var errores = ValidadorDeFixture.Validar(fixture);

        Assert.Equal(7, errores.Count);
        Assert.Equivalent(
            new[]
            {
                new ErrorDeFixture("equipos[4].codigo", "codigo_de_equipo_duplicado"),
                new ErrorDeFixture("equipos[1].jugadores[1].codigo", "codigo_de_jugador_duplicado"),
                new ErrorDeFixture("fases[1].orden", "orden_de_fase_duplicado"),
                new ErrorDeFixture("partidos[5].fase", "fase_inexistente"),
                new ErrorDeFixture("partidos[1]", "pareja_repetida"),
                new ErrorDeFixture("partidos[3]", "equipo_repetido_en_jornada"),
                new ErrorDeFixture("partidos[4].cancelado", "partido_de_equipo_retirado_sin_cancelar"),
            },
            errores);
    }

    [Fact] // T-02
    public void Un_codigo_de_equipo_inexistente_en_un_partido_se_reporta_con_su_ruta()
    {
        var error = Unico(Fixture([F()], CuatroEquipos(), [P("A", "Z", 1)]));

        Assert.Equal(new ErrorDeFixture("partidos[0].visitante", "equipo_inexistente"), error);
    }

    [Fact] // T-02
    public void Sin_fases_y_con_un_solo_equipo_devuelve_exactamente_los_dos_errores_de_la_regla_g()
    {
        var errores = ValidadorDeFixture.Validar(Fixture([], [E("A")], []));

        Assert.Equal(2, errores.Count);
        Assert.Equivalent(
            new[] { new ErrorDeFixture("fases", "sin_fases"), new ErrorDeFixture("equipos", "equipos_insuficientes") },
            errores);
    }

    [Fact] // T-02 (R-12 dentro del validador)
    public void Los_datos_invalidos_de_R_12_se_reportan_todos_a_la_vez()
    {
        var fixture = new FixtureDescripcion(
            " ",
            [new FaseDescripcion(0, nameof(TipoDeFase.TodosContraTodos), 3)],
            [new EquipoDescripcion("EQ 1", "Equipo Uno", false, [new JugadorDescripcion("J-1", "")]), E("B")],
            [new PartidoDescripcion(1, 0, "B", "B", null, false)]);

        var errores = ValidadorDeFixture.Validar(fixture);

        Assert.Equivalent(
            new[]
            {
                new ErrorDeFixture("nombre", "nombre_invalido"),
                new ErrorDeFixture("fases[0].orden", "orden_invalido"),
                new ErrorDeFixture("fases[0].vueltas", "vueltas_invalidas"),
                new ErrorDeFixture("equipos[0].codigo", "codigo_invalido"),
                new ErrorDeFixture("equipos[0].jugadores[0].nombre", "nombre_invalido"),
                new ErrorDeFixture("partidos[0].jornada", "jornada_invalida"),
                new ErrorDeFixture("partidos[0].visitante", "local_igual_a_visitante"),
            },
            errores);
    }

    [Theory] // T-02 (R-17: hora sin desplazamiento)
    [InlineData("2026-11-08T15:00:00", "hora_sin_desplazamiento")]
    [InlineData("2026-11-08T15:00", "hora_sin_desplazamiento")]
    [InlineData("mañana", "hora_invalida")]
    [InlineData("2026-11-08", "hora_invalida")]
    public void Una_hora_sin_desplazamiento_o_ilegible_es_un_error(string hora, string codigo)
    {
        var partido = new PartidoDescripcion(1, 1, "A", "B", hora, false);

        var error = Unico(Fixture([F()], CuatroEquipos(), [partido]));

        Assert.Equal(new ErrorDeFixture("partidos[0].programadoEn", codigo), error);
    }

    [Theory] // T-02 (R-17: hora válida)
    [InlineData("2026-11-08T15:00:00Z")]
    [InlineData("2026-11-08T15:00:00-05:00")]
    [InlineData("2026-11-08T15:00:00.123+02:30")]
    public void Una_hora_con_desplazamiento_explicito_es_valida(string hora) =>
        SinErrores(Fixture([F()], CuatroEquipos(), [new PartidoDescripcion(1, 1, "A", "B", hora, false)]));

    [Theory] // T-02 (R-16)
    [InlineData("fases", "demasiadas_fases")]
    [InlineData("equipos", "demasiados_equipos")]
    [InlineData("jugadores", "demasiados_jugadores")]
    [InlineData("partidos", "demasiados_partidos")]
    public void Superar_un_limite_de_tamano_se_reporta(string coleccion, string codigo)
    {
        var fases = Enumerable.Range(1, coleccion == "fases" ? 9 : 1).Select(o => F(o));
        var equipos = Enumerable.Range(1, coleccion == "equipos" ? 65 : 4).Select(i => E($"E{i}"));
        if (coleccion == "jugadores")
            equipos = [E("E1", false, [.. Enumerable.Range(1, 61).Select(i => $"j{i}")]), E("E2")];
        var partidos = coleccion == "partidos"
            ? Enumerable.Range(1, 1001).Select(i => P("E1", "E2", i))
            : [P("E1", "E2", 1)];

        var errores = ValidadorDeFixture.Validar(Fixture(fases, equipos, partidos));

        Assert.Contains(errores, e => e.Codigo == codigo && e.Ruta == coleccion);
    }

    [Fact] // T-03
    public void Con_una_vuelta_A_B_mas_B_A_es_un_error()
    {
        var error = Unico(Fixture([F(1, vueltas: 1)], CuatroEquipos(), [P("A", "B", 1), P("B", "A", 2)]));

        Assert.Equal(new ErrorDeFixture("partidos[1]", "pareja_repetida"), error);
    }

    [Fact] // T-03
    public void Con_dos_vueltas_A_B_mas_B_A_es_valido() =>
        SinErrores(Fixture([F(1, vueltas: 2)], CuatroEquipos(), [P("A", "B", 1), P("B", "A", 2)]));

    [Fact] // T-03
    public void Con_dos_vueltas_A_B_dos_veces_es_un_error()
    {
        var error = Unico(Fixture([F(1, vueltas: 2)], CuatroEquipos(), [P("A", "B", 1), P("A", "B", 2)]));

        Assert.Equal(new ErrorDeFixture("partidos[1]", "pareja_repetida"), error);
    }

    [Fact] // T-32
    public void Un_partido_cancelado_libera_la_jornada_para_la_regla_f() =>
        SinErrores(Fixture([F()], CuatroEquipos(), [P("A", "B", 3, cancelado: true), P("B", "C", 3)]));

    [Fact] // T-32
    public void Un_partido_cancelado_conserva_la_identidad_de_su_pareja_para_la_regla_e()
    {
        var error = Unico(Fixture(
            [F(1, vueltas: 1)], CuatroEquipos(), [P("A", "B", 3, cancelado: true), P("A", "B", 4)]));

        Assert.Equal(new ErrorDeFixture("partidos[1]", "pareja_repetida"), error);
    }

    [Fact] // T-32
    public void Dos_partidos_programados_del_mismo_equipo_en_la_misma_jornada_son_un_error()
    {
        var error = Unico(Fixture([F()], CuatroEquipos(), [P("B", "C", 3), P("B", "D", 3)]));

        Assert.Equal(new ErrorDeFixture("partidos[1]", "equipo_repetido_en_jornada"), error);
    }
}
