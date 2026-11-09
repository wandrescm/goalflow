using GoalFlow.Domain.Torneos;

namespace GoalFlow.Tests.Torneos;

/// <summary>Spec 006 T-24 (parte unitaria): la regla de rectificación de R-14 y de C-11, C-21 y C-27.</summary>
public class ReglaDeRectificacionTests
{
    private const EstadoDePartido P = EstadoDePartido.Programado;
    private const EstadoDePartido J = EstadoDePartido.Jugado;
    private const EstadoDePartido S = EstadoDePartido.Suspendido;
    private const EstadoDePartido A = EstadoDePartido.Adjudicado;

    private static PlanillaResuelta Hoja(
        EstadoDePartido estado, int goles = 1, MotivoDeRectificacion? motivo = null, bool conTarjeta = false)
    {
        if (estado == P) return new(P, null, null, null, null, null, motivo, null, [], []);
        return new(
            estado,
            estado == A ? null : goles, estado == A ? null : 0,
            estado == A ? 2 + goles : null, estado == A ? 0 : null,
            estado switch { S => MotivoDeResultado.Clima, A => MotivoDeResultado.WalkOver, _ => null },
            motivo,
            estado == A ? null : true,
            [],
            conTarjeta ? [new FilaDeTarjeta("J-07", ColorDeTarjeta.Amarilla, 1)] : []);
    }

    // ---- Transiciones ordinarias: sin motivo de rectificación ----

    public static TheoryData<EstadoDePartido, EstadoDePartido> Ordinarias() => new()
    {
        { P, J }, { P, S }, { P, A }, // primer registro
        { S, J }, { S, A }, // completar o resolver una suspensión
        { J, J }, { S, S }, { A, A }, // corrección en el mismo estado
    };

    [Theory]
    [MemberData(nameof(Ordinarias))]
    public void T24_una_transicion_ordinaria_sin_motivo_es_valida(EstadoDePartido desde, EstadoDePartido hacia)
    {
        var vigente = desde == P ? null : Hoja(desde, goles: 1);

        Assert.True(ReglaDeRectificacion.EsValida(desde, vigente, Hoja(hacia, goles: 2)));
    }

    [Theory]
    [MemberData(nameof(Ordinarias))]
    public void T24_una_transicion_ordinaria_con_motivo_es_invalida(EstadoDePartido desde, EstadoDePartido hacia)
    {
        var vigente = desde == P ? null : Hoja(desde, goles: 1);

        Assert.False(ReglaDeRectificacion.EsValida(
            desde, vigente, Hoja(hacia, goles: 2, motivo: MotivoDeRectificacion.ErrorDeRegistro)));
    }

    // ---- Transiciones excepcionales: motivo obligatorio ----

    public static TheoryData<EstadoDePartido, EstadoDePartido> Excepcionales() => new()
    {
        { J, S }, { J, A }, { A, J }, { A, S }, // entre estados de resultado
        { J, P }, { S, P }, { A, P }, // retiro de la planilla
    };

    [Theory]
    [MemberData(nameof(Excepcionales))]
    public void T24_una_transicion_excepcional_sin_motivo_es_invalida(EstadoDePartido desde, EstadoDePartido hacia)
    {
        Assert.False(ReglaDeRectificacion.EsValida(desde, Hoja(desde), Hoja(hacia, goles: 2)));
    }

    [Theory]
    [MemberData(nameof(Excepcionales))]
    public void T24_una_transicion_excepcional_con_motivo_es_valida(EstadoDePartido desde, EstadoDePartido hacia)
    {
        Assert.True(ReglaDeRectificacion.EsValida(
            desde, Hoja(desde), Hoja(hacia, goles: 2, motivo: MotivoDeRectificacion.ErrorDeRegistro)));
    }

    [Fact]
    public void T24_Jugado_a_Adjudicado_con_DecisionReglamentaria_es_valida()
    {
        Assert.True(ReglaDeRectificacion.EsValida(
            J, Hoja(J), Hoja(A, motivo: MotivoDeRectificacion.DecisionReglamentaria)));
    }

    // ---- Programado → Programado ----

    [Fact]
    public void T24_Programado_a_Programado_con_otro_contenido_es_invalido_con_o_sin_motivo()
    {
        var retirada = Hoja(P, motivo: MotivoDeRectificacion.PartidoEquivocado);

        Assert.False(ReglaDeRectificacion.EsValida(P, retirada, Hoja(P, motivo: MotivoDeRectificacion.ErrorDeRegistro)));
        Assert.False(ReglaDeRectificacion.EsValida(P, retirada, Hoja(P)));
        Assert.False(ReglaDeRectificacion.EsValida(P, null, Hoja(P, motivo: MotivoDeRectificacion.ErrorDeRegistro)));
    }

    [Fact]
    public void T24_tras_un_retiro_el_primer_registro_es_ordinario()
    {
        var retirada = Hoja(P, motivo: MotivoDeRectificacion.PartidoEquivocado);

        Assert.True(ReglaDeRectificacion.EsValida(P, retirada, Hoja(J)));
        Assert.False(ReglaDeRectificacion.EsValida(P, retirada, Hoja(J, motivo: MotivoDeRectificacion.Otro)));
    }

    // ---- C-27: el motivo no se edita solo y no se arrastra ----

    [Fact]
    public void T24_cambiar_solo_el_motivo_es_invalido_agregado_quitado_o_cambiado()
    {
        var sin = Hoja(A);
        var con = Hoja(A, motivo: MotivoDeRectificacion.DecisionReglamentaria);
        var otro = Hoja(A, motivo: MotivoDeRectificacion.ErrorDeRegistro);

        Assert.False(ReglaDeRectificacion.EsValida(A, sin, con)); // agregado
        Assert.False(ReglaDeRectificacion.EsValida(A, con, sin)); // quitado
        Assert.False(ReglaDeRectificacion.EsValida(A, con, otro)); // cambiado
    }

    [Fact]
    public void T24_tras_una_version_excepcional_un_cambio_con_el_motivo_arrastrado_es_invalido()
    {
        var versionN = Hoja(A, motivo: MotivoDeRectificacion.DecisionReglamentaria, conTarjeta: false);
        var conTarjetaCambiada = Hoja(A, motivo: MotivoDeRectificacion.DecisionReglamentaria, conTarjeta: true);

        Assert.False(ReglaDeRectificacion.EsValida(A, versionN, conTarjetaCambiada));
    }

    [Fact]
    public void T24_tras_una_version_excepcional_un_cambio_sin_motivo_es_valido_y_la_version_anterior_conserva_el_suyo()
    {
        var versionN = Hoja(A, motivo: MotivoDeRectificacion.DecisionReglamentaria);
        var siguiente = Hoja(A, conTarjeta: true);

        Assert.True(ReglaDeRectificacion.EsValida(A, versionN, siguiente));
        Assert.Equal(MotivoDeRectificacion.DecisionReglamentaria, versionN.MotivoRectificacion);
        Assert.Null(siguiente.MotivoRectificacion);
    }

    [Fact]
    public void T24_tras_una_version_excepcional_un_nuevo_cambio_excepcional_exige_su_propio_motivo()
    {
        var versionN = Hoja(A, motivo: MotivoDeRectificacion.DecisionReglamentaria);

        Assert.False(ReglaDeRectificacion.EsValida(A, versionN, Hoja(J)));
        Assert.True(ReglaDeRectificacion.EsValida(A, versionN, Hoja(J, motivo: MotivoDeRectificacion.ErrorDeRegistro)));
    }

    // ---- Igualdad normalizada (R-17), de la que depende el orden de evaluación ----

    [Fact]
    public void T24_la_igualdad_ignora_el_orden_de_las_filas()
    {
        var a = Hoja(J) with
        {
            Goles = [new(TipoDeGol.Jugador, LadoDelPartido.Local, "J-07", 1), new(TipoDeGol.SinAutor, LadoDelPartido.Visitante, null, 1)],
        };
        var b = a with { Goles = [.. a.Goles.Reverse()] };

        Assert.True(a.Equivale(b));
    }

    [Fact]
    public void T24_la_igualdad_distingue_el_motivo_de_rectificacion_pero_la_variante_sin_motivo_no()
    {
        var sin = Hoja(A);
        var con = Hoja(A, motivo: MotivoDeRectificacion.Otro);

        Assert.False(sin.Equivale(con));
        Assert.True(sin.MismoContenidoSinMotivoDeRectificacion(con));
    }
}
