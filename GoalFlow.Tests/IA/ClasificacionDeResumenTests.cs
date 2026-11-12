using GoalFlow.Application.IA;
using GoalFlow.Application.Torneos.Contracts;

namespace GoalFlow.Tests.IA;

/// <summary>Spec 007 T-03 (R-10): qué produce A-01 para cada estado (ADR-12 §2.2).</summary>
public class ClasificacionDeResumenTests
{
    [Theory]
    [InlineData("a) Jugado con goleador", ClasificacionDeResumen.Narrativa)]
    [InlineData("b) Jugado 2-1 con todos los goles sin autor", ClasificacionDeResumen.Plantilla)]
    [InlineData("c) Adjudicado W.O. sin marcador deportivo", ClasificacionDeResumen.Plantilla)]
    [InlineData("d) Adjudicado con marcador deportivo", ClasificacionDeResumen.Plantilla)]
    [InlineData("e) Programado (retirado)", ClasificacionDeResumen.Ninguno)]
    [InlineData("f) Jugado 0-0 sin tarjetas", ClasificacionDeResumen.Plantilla)]
    public void T03_los_cinco_casos_del_propietario_y_el_cero_a_cero(string caso, ClasificacionDeResumen esperada)
    {
        var datos = caso[0] switch
        {
            'a' => DatosDePrueba.Vacio() with
            {
                Marcador = new MarcadorDelPartido(1, 0),
                Goles = [new GolDelPartido(LadoDelEquipo.Local, "Jugador Uno", "JUG-001", 1)],
            },
            'b' => DatosDePrueba.Vacio() with
            {
                Marcador = new MarcadorDelPartido(2, 1),
                GolesSinAutor = new MarcadorDelPartido(2, 1),
            },
            'c' => DatosDePrueba.Vacio(CodigosDeTorneos.Adjudicado) with
            {
                Marcador = null, Oficial = new MarcadorDelPartido(3, 0), Motivo = CodigosDeTorneos.WalkOver,
            },
            'd' => DatosDePrueba.Vacio(CodigosDeTorneos.Adjudicado) with
            {
                Marcador = new MarcadorDelPartido(1, 2), Oficial = new MarcadorDelPartido(3, 0), Motivo = CodigosDeTorneos.Sancion,
            },
            'e' => DatosDePrueba.Vacio(CodigosDeTorneos.Programado) with { Marcador = null },
            'f' => DatosDePrueba.Vacio(),
            _ => throw new InvalidOperationException(caso),
        };

        Assert.Equal(esperada, ClasificadorDeResumen.Clasificar(datos));
    }

    [Theory] // Un solo evento con nombre basta: autogol o tarjeta.
    [InlineData("autogol")]
    [InlineData("tarjeta")]
    public void T03_un_autogol_o_una_tarjeta_solos_ya_son_narrativa(string evento)
    {
        var datos = DatosDePrueba.Vacio() with
        {
            Marcador = new MarcadorDelPartido(evento == "autogol" ? 0 : 0, evento == "autogol" ? 1 : 0),
            Autogoles = evento == "autogol" ? [new AutogolDelPartido("Jugador Uno", "JUG-001", LadoDelEquipo.Local, 1)] : [],
            Tarjetas = evento == "tarjeta"
                ? [new TarjetaDelPartido(LadoDelEquipo.Local, "Jugador Uno", "JUG-001", CodigosDeTorneos.Amarilla, 1)]
                : [],
        };

        Assert.Equal(ClasificacionDeResumen.Narrativa, ClasificadorDeResumen.Clasificar(datos));
    }

    [Theory]
    [InlineData(CodigosDeTorneos.Suspendido)]
    [InlineData(CodigosDeTorneos.Cancelado)]
    [InlineData(CodigosDeTorneos.Programado)]
    public void T03_programado_suspendido_y_cancelado_no_producen_nada_aunque_traigan_eventos(string estado)
    {
        var datos = DatosDePrueba.ConAutogol() with { Estado = estado };

        Assert.Equal(ClasificacionDeResumen.Ninguno, ClasificadorDeResumen.Clasificar(datos));
    }

    [Fact]
    public void T03_un_estado_desconocido_no_se_clasifica_en_silencio()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ClasificadorDeResumen.Clasificar(DatosDePrueba.Vacio("Perdido")));
    }
}
