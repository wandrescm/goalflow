using GoalFlow.Domain.Torneos;

namespace GoalFlow.Tests.Torneos;

/// <summary>Spec 009 T-09 (unitaria): el validador puro del reglamento (R-05, R-06).</summary>
public class ValidadorDeReglamentoTests
{
    private static AdjudicacionDeMotivoDescripcion Completa(
        int ganador = 3, int perdedor = 0, int empate = 1, int deduccionPerdedor = 0, int deduccionEmpate = 0,
        bool? tarjetasEnFairPlay = true) =>
        new(ganador, perdedor, empate, deduccionPerdedor, deduccionEmpate, true, true, true, true, tarjetasEnFairPlay);

    private static DesempateDescripcion Desempate(params string[] criterios) => new([.. criterios], true);

    private static ReglamentoDescripcion Valido(
        string estado = "Provisional", PuntosDescripcion? puntos = null, DesempateDescripcion? desempate = null,
        FairPlayDescripcion? fairPlay = null, AdjudicacionDescripcion? adjudicacion = null) =>
        new(estado,
            puntos ?? new PuntosDescripcion(3, 1, 0),
            desempate ?? Desempate("PuntosDirectos", "DiferenciaDeGolesDirecta", "GolesAFavorDirectos", "DiferenciaDeGoles", "GolesAFavor", "FairPlay"),
            fairPlay ?? new FairPlayDescripcion(1, 2, 3),
            adjudicacion ?? new AdjudicacionDescripcion(Completa(), Completa(), Completa()));

    private static IReadOnlyList<ErrorDeReglamento> Errores(ReglamentoDescripcion reglamento) =>
        ValidadorDeReglamento.Validar(reglamento).Errores;

    [Fact] // T-09: un cuerpo válido no devuelve errores y trae el reglamento canónico
    public void Un_reglamento_valido_no_devuelve_errores_y_trae_su_contenido_canonico()
    {
        var resultado = ValidadorDeReglamento.Validar(Valido());

        Assert.Empty(resultado.Errores);
        Assert.Equal(EstadoDeReglamento.Provisional, resultado.Estado);
        var contenido = Assert.IsType<ContenidoDeReglamento>(resultado.Contenido);
        Assert.Equal(new PuntosDeReglamento(3, 1, 0), contenido.Puntos);
        Assert.Equal(new FairPlayDeReglamento(1, 2, 3), contenido.FairPlay);
        Assert.Equal(
            [CriterioDeDesempate.PuntosDirectos, CriterioDeDesempate.DiferenciaDeGolesDirecta, CriterioDeDesempate.GolesAFavorDirectos,
             CriterioDeDesempate.DiferenciaDeGoles, CriterioDeDesempate.GolesAFavor, CriterioDeDesempate.FairPlay],
            contenido.Desempate!.Criterios);
        Assert.True(contenido.Desempate.DirectoRecursivo);
    }

    [Fact] // T-09: el cuerpo 1, con una violación en un miembro distinto por regla, da exactamente 8 errores con su ruta
    public void El_cuerpo_1_con_una_violacion_de_cada_regla_devuelve_exactamente_ocho_errores_con_su_ruta()
    {
        var reglamento = new ReglamentoDescripcion(
            Estado: "Oficial",                                                                      // (a)
            Puntos: new PuntosDescripcion(1, 1, 0),                                                 // (c)
            Desempate: Desempate(
                "PuntosDirectos", "DiferenciaDeGoles", "DiferenciaDeGolesDirecta", "GolesAFavor", "GolesAFavor", "FairPlay"), // (e) (f) (g)
            FairPlay: null,                                                                         // (g): sin sección fairPlay
            Adjudicacion: new AdjudicacionDescripcion(
                WalkOver: Completa(deduccionPerdedor: 11),                                          // (b)
                Sancion: Completa(ganador: 0, empate: 0, perdedor: 1),                              // (d)
                Otro: Completa(tarjetasEnFairPlay: null)));                                         // (i)

        var resultado = ValidadorDeReglamento.Validar(reglamento);

        Assert.Equal(8, resultado.Errores.Count);
        Assert.Null(resultado.Contenido);
        Assert.Null(resultado.Estado);
        var esperados = new HashSet<(string, string)>
        {
            ("reglamento.estado", CodigosDeReglamento.EstadoInvalido),
            ("reglamento.adjudicacion.WalkOver.deduccionPerdedor", CodigosDeReglamento.ValorFueraDeRango),
            ("reglamento.puntos", CodigosDeReglamento.PuntosNoDecrecientes),
            ("reglamento.adjudicacion.Sancion", CodigosDeReglamento.AdjudicacionNoDecreciente),
            ("reglamento.desempate.criterios[4]", CodigosDeReglamento.CriterioRepetido),
            ("reglamento.desempate.criterios", CodigosDeReglamento.CriteriosDirectosNoContiguos),
            ("reglamento.fairPlay", CodigosDeReglamento.FairPlayRequerido),
            ("reglamento.adjudicacion.Otro.tarjetasEnFairPlay", CodigosDeReglamento.MiembroRequerido),
        };
        Assert.True(esperados.SetEquals(resultado.Errores.Select(e => (e.Ruta, e.Codigo))),
            "Errores obtenidos: " + string.Join(", ", resultado.Errores.Select(e => $"{e.Ruta}:{e.Codigo}")));
    }

    [Fact] // T-09: el cuerpo 2, válido salvo fairPlay.roja 101 (h), devuelve exactamente 1 error
    public void El_cuerpo_2_con_un_peso_de_fair_play_fuera_de_rango_devuelve_exactamente_un_error()
    {
        var error = Assert.Single(Errores(Valido(fairPlay: new FairPlayDescripcion(1, 2, 101))));

        Assert.Equal(new ErrorDeReglamento("reglamento.fairPlay.roja", CodigosDeReglamento.ValorFueraDeRango), error);
    }

    [Fact] // T-09: una lista de criterios vacía es válida (R-04, C-13)
    public void Una_lista_de_criterios_vacia_con_directoRecursivo_false_es_valida()
    {
        var resultado = ValidadorDeReglamento.Validar(Valido(desempate: new DesempateDescripcion([], false), fairPlay: null));

        Assert.Empty(resultado.Errores);
        Assert.Empty(resultado.Contenido!.Desempate!.Criterios);
        Assert.False(resultado.Contenido.Desempate.DirectoRecursivo);
    }

    [Fact] // T-09: un miembro ausente de una sección presente es un error, nunca un valor por defecto (R-06 i)
    public void Un_directoRecursivo_ausente_es_un_error_y_no_un_valor_por_defecto()
    {
        var error = Assert.Single(Errores(Valido(desempate: new DesempateDescripcion([], null), fairPlay: null)));

        Assert.Equal(new ErrorDeReglamento("reglamento.desempate.directoRecursivo", CodigosDeReglamento.MiembroRequerido), error);
    }

    [Fact] // T-09: un reglamento sin secciones opcionales es válido (R-03: solo el estado es obligatorio)
    public void Un_reglamento_con_solo_el_estado_es_valido_y_no_asume_ninguna_seccion()
    {
        var resultado = ValidadorDeReglamento.Validar(new ReglamentoDescripcion("Confirmado", null, null, null, null));

        Assert.Empty(resultado.Errores);
        Assert.Equal(EstadoDeReglamento.Confirmado, resultado.Estado);
        Assert.Equal(new ContenidoDeReglamento(null, null, null, null), resultado.Contenido);
    }

    [Fact] // R-07 c: el orden de los criterios importa en la comparación canónica
    public void La_igualdad_canonica_distingue_el_orden_de_los_criterios()
    {
        var a = ValidadorDeReglamento.Validar(Valido(desempate: Desempate("DiferenciaDeGoles", "GolesAFavor"), fairPlay: null)).Contenido;
        var igual = ValidadorDeReglamento.Validar(Valido(desempate: Desempate("DiferenciaDeGoles", "GolesAFavor"), fairPlay: null)).Contenido;
        var invertido = ValidadorDeReglamento.Validar(Valido(desempate: Desempate("GolesAFavor", "DiferenciaDeGoles"), fairPlay: null)).Contenido;

        Assert.Equal(a, igual);
        Assert.NotEqual(a, invertido);
    }
}
