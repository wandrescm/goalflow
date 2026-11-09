using GoalFlow.Domain.Torneos;

namespace GoalFlow.Tests.Torneos;

/// <summary>Spec 006 T-01 a T-03: el validador puro de la planilla (R-11, R-12, R-13). Datos inventados (J-07, J-30).</summary>
public class ValidadorDePlanillaTests
{
    // Local: J-07 y J-08. Visitante: J-21 y J-30.
    private static readonly IReadOnlyDictionary<string, LadoDelPartido> Jugadores = new Dictionary<string, LadoDelPartido>
    {
        ["J-07"] = LadoDelPartido.Local,
        ["J-08"] = LadoDelPartido.Local,
        ["J-21"] = LadoDelPartido.Visitante,
        ["J-30"] = LadoDelPartido.Visitante,
    };

    private static readonly MarcadorDescripcion SinGolesSinAutor = new(0, 0);

    private static PlanillaDescripcion Jugado(
        int local, int visitante,
        GolDeJugadorDescripcion[]? goles = null, GolDeJugadorDescripcion[]? autogoles = null,
        MarcadorDescripcion? sinAutor = null, TarjetaDescripcion[]? tarjetas = null, bool? informadas = true) =>
        new("Jugado", new(local, visitante), null, null, null, informadas,
            goles ?? [], autogoles ?? [], sinAutor ?? SinGolesSinAutor, tarjetas ?? []);

    private static ResultadoDeValidacionDePlanilla Validar(PlanillaDescripcion planilla) =>
        ValidadorDePlanilla.Validar(planilla, Jugadores);

    private static void ErroresExactos(ResultadoDeValidacionDePlanilla resultado, params (string Ruta, string Codigo)[] esperados)
    {
        Assert.Null(resultado.Planilla);
        Assert.Equal(
            esperados.OrderBy(e => e.Ruta, StringComparer.Ordinal).ThenBy(e => e.Codigo, StringComparer.Ordinal).ToArray(),
            resultado.Errores.Select(e => (e.Ruta, e.Codigo)).OrderBy(e => e.Ruta, StringComparer.Ordinal)
                .ThenBy(e => e.Codigo, StringComparer.Ordinal).ToArray());
    }

    private static void EsValida(ResultadoDeValidacionDePlanilla resultado)
    {
        Assert.Empty(resultado.Errores);
        Assert.NotNull(resultado.Planilla);
    }

    // ---- T-01 ----

    [Fact]
    public void T01_una_planilla_Jugado_2_0_con_una_violacion_de_cada_regla_devuelve_exactamente_5_errores_con_su_ruta()
    {
        var planilla = Jugado(2, 0,
            goles: [new("J-07", 2)],
            sinAutor: new(0, 1), // (g) solo el lado visitante: 0 + 0 + 1 ≠ 0
            tarjetas:
            [
                new("J-07", "Amarilla", 1),
                new("J-07", "Amarilla", 1), // (h) el mismo jugador y color dos veces
                new("J-99", "Roja", 1), // (f) jugador desconocido
                new("J-30", "Azul", 10), // (i) cantidad de 10
            ]) with
        {
            Oficial = new(2, 0), // (a) un Jugado no lleva marcador oficial
        };

        var resultado = Validar(planilla);

        ErroresExactos(resultado,
            ("oficial", CodigosDePlanilla.OficialNoPermitido),
            ("marcador.visitante", CodigosDePlanilla.SumaDeGolesDistintaDelMarcador),
            ("tarjetas[1].jugador", CodigosDePlanilla.JugadorRepetido),
            ("tarjetas[2].jugador", CodigosDePlanilla.JugadorFueraDelPartido),
            ("tarjetas[3].cantidad", CodigosDePlanilla.CantidadInvalida));
    }

    [Fact]
    public void T01_una_planilla_valida_no_devuelve_errores()
    {
        var resultado = Validar(Jugado(2, 0, goles: [new("J-07", 2)], tarjetas: [new("J-30", "Amarilla", 1)]));

        EsValida(resultado);
    }

    // ---- T-02 ----

    [Fact]
    public void T02_el_3_1_con_un_autogol_del_visitante_es_valido_y_se_resuelve_en_exactamente_3_filas()
    {
        var planilla = Jugado(3, 1,
            goles: [new("J-07", 2), new("J-30", 1)],
            autogoles: [new("J-21", 1)]);

        var resultado = Validar(planilla);

        EsValida(resultado);
        Assert.Equal(
            new[]
            {
                new FilaDeGol(TipoDeGol.Jugador, LadoDelPartido.Local, "J-07", 2),
                new FilaDeGol(TipoDeGol.Autogol, LadoDelPartido.Local, "J-21", 1),
                new FilaDeGol(TipoDeGol.Jugador, LadoDelPartido.Visitante, "J-30", 1),
            }.ToHashSet(),
            resultado.Planilla!.Goles.ToHashSet());
        Assert.Equal(3, resultado.Planilla.Goles.Count);
    }

    [Fact]
    public void T02_el_mismo_autogol_listado_como_gol_falla_la_suma_en_los_dos_lados_con_2_errores()
    {
        var planilla = Jugado(3, 1, goles: [new("J-07", 2), new("J-21", 1), new("J-30", 1)]);

        var resultado = Validar(planilla);

        ErroresExactos(resultado,
            ("marcador.local", CodigosDePlanilla.SumaDeGolesDistintaDelMarcador),
            ("marcador.visitante", CodigosDePlanilla.SumaDeGolesDistintaDelMarcador));
    }

    [Fact]
    public void T02_los_goles_sin_autor_se_resuelven_en_una_fila_por_lado_acreditado()
    {
        var planilla = Jugado(2, 1, goles: [new("J-07", 1)], sinAutor: new(1, 1), informadas: false);

        var resultado = Validar(planilla);

        EsValida(resultado);
        Assert.Equal(
            new[]
            {
                new FilaDeGol(TipoDeGol.Jugador, LadoDelPartido.Local, "J-07", 1),
                new FilaDeGol(TipoDeGol.SinAutor, LadoDelPartido.Local, null, 1),
                new FilaDeGol(TipoDeGol.SinAutor, LadoDelPartido.Visitante, null, 1),
            }.ToHashSet(),
            resultado.Planilla!.Goles.ToHashSet());
    }

    // ---- T-03 ----

    [Fact]
    public void T03_una_adjudicacion_WalkOver_3_0_sin_marcador_ni_goles_ni_bandera_es_valida()
    {
        var planilla = new PlanillaDescripcion("Adjudicado", null, new(3, 0), "WalkOver", null, null, [], [], SinGolesSinAutor, []);

        var resultado = Validar(planilla);

        EsValida(resultado);
        Assert.Equal(3, resultado.Planilla!.OficialLocal);
        Assert.Equal(MotivoDeResultado.WalkOver, resultado.Planilla.Motivo);
    }

    [Fact]
    public void T03_la_misma_adjudicacion_con_un_gol_es_invalida_por_la_regla_e()
    {
        var planilla = new PlanillaDescripcion(
            "Adjudicado", null, new(3, 0), "WalkOver", null, null, [new("J-07", 1)], [], SinGolesSinAutor, []);

        ErroresExactos(Validar(planilla), ("goles[0]", CodigosDePlanilla.ContenidoSinMarcador));
    }

    [Fact]
    public void T03_una_suspension_1_0_sin_motivo_es_invalida_por_la_regla_b()
    {
        var planilla = new PlanillaDescripcion(
            "Suspendido", new(1, 0), null, null, null, true, [new("J-07", 1)], [], SinGolesSinAutor, []);

        ErroresExactos(Validar(planilla), ("motivo", CodigosDePlanilla.MotivoRequerido));
    }

    [Fact]
    public void T03_una_adjudicacion_sin_marcador_oficial_es_invalida_por_la_regla_c()
    {
        var planilla = new PlanillaDescripcion("Adjudicado", null, null, "Sancion", null, null, [], [], SinGolesSinAutor, []);

        ErroresExactos(Validar(planilla), ("oficial", CodigosDePlanilla.OficialRequerido));
    }

    [Fact]
    public void T03_un_Programado_con_marcador_es_invalido_por_la_regla_d()
    {
        var planilla = new PlanillaDescripcion("Programado", new(1, 0), null, null, null, true, [], [], SinGolesSinAutor, []);

        var resultado = Validar(planilla);

        Assert.Contains(resultado.Errores, e => e is { Ruta: "marcador", Codigo: CodigosDePlanilla.MarcadorNoPermitido });
        Assert.Null(resultado.Planilla);
    }

    [Fact]
    public void T03_un_Jugado_sin_la_bandera_de_tarjetas_es_invalido_por_la_regla_j()
    {
        ErroresExactos(Validar(Jugado(0, 0, informadas: null)), ("tarjetasInformadas", CodigosDePlanilla.TarjetasInformadasRequerido));
    }

    [Fact]
    public void T03_la_bandera_en_false_con_una_tarjeta_es_invalida_por_la_regla_j()
    {
        var planilla = Jugado(0, 0, tarjetas: [new("J-07", "Amarilla", 1)], informadas: false);

        ErroresExactos(Validar(planilla), ("tarjetas", CodigosDePlanilla.TarjetasSinInformar));
    }

    [Fact]
    public void T03_la_bandera_en_true_sin_tarjetas_es_valida_y_significa_cero_tarjetas_confirmadas()
    {
        var resultado = Validar(Jugado(0, 0, informadas: true));

        EsValida(resultado);
        Assert.True(resultado.Planilla!.TarjetasInformadas);
        Assert.Empty(resultado.Planilla.Tarjetas);
    }

    [Theory]
    [InlineData("Jugado", "Clima", CodigosDePlanilla.MotivoNoPermitido)]
    [InlineData("Suspendido", "WalkOver", CodigosDePlanilla.MotivoInvalido)]
    [InlineData("Adjudicado", "Clima", CodigosDePlanilla.MotivoInvalido)]
    [InlineData("Suspendido", "Inventado", CodigosDePlanilla.MotivoInvalido)]
    public void T03_el_motivo_debe_ser_uno_de_los_de_su_estado(string estado, string motivo, string codigo)
    {
        var marcador = estado == "Adjudicado" ? null : new MarcadorDescripcion(0, 0);
        var oficial = estado == "Adjudicado" ? new MarcadorDescripcion(3, 0) : null;
        var planilla = new PlanillaDescripcion(
            estado, marcador, oficial, motivo, null, marcador is null ? null : true, [], [], SinGolesSinAutor, []);

        ErroresExactos(Validar(planilla), ("motivo", codigo));
    }

    [Fact]
    public void T03_un_estado_desconocido_o_Cancelado_es_invalido()
    {
        foreach (var estado in new[] { "Cancelado", "Perdido", "" })
        {
            var planilla = new PlanillaDescripcion(estado, null, null, null, null, null, [], [], SinGolesSinAutor, []);

            ErroresExactos(Validar(planilla), ("estado", CodigosDePlanilla.EstadoInvalido));
        }
    }

    [Fact]
    public void T03_una_planilla_se_valida_por_sus_reglas_sin_importar_el_estado_guardado()
    {
        // R-11: "independientemente del estado guardado". Dos validaciones de la misma planilla dan lo mismo.
        var planilla = Jugado(1, 0, goles: [new("J-07", 1)]);

        var primera = Validar(planilla);
        var segunda = Validar(planilla);

        Assert.Equal(primera.Errores, segunda.Errores);
        Assert.True(primera.Planilla!.Equivale(segunda.Planilla!));
    }
}
