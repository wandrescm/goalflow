using GoalFlow.Application.IA;
using GoalFlow.Application.Torneos.Contracts;

namespace GoalFlow.Tests.IA;

/// <summary>Spec 007 T-04 (R-11): textos dorados de las plantillas y del borrador extendido. Funciones puras de la entrada canónica.</summary>
public class PlantillasDeResumenTests
{
    private const int LongitudMaxima = 4000;

    private static EntradaCanonica Entrada(DatosDePartidoParaResumen datos) => EntradaCanonica.Construir(datos);

    private static DatosDePartidoParaResumen Jugado(int local, int visitante) =>
        DatosDePrueba.Vacio() with { Marcador = new MarcadorDelPartido(local, visitante) };

    [Fact]
    public void T04_plantilla_de_Jugado_sin_goles_sin_autor()
    {
        Assert.Equal("Equipo Uno 0-0 Equipo Dos.", PlantillasDeResumen.Plantilla(Entrada(Jugado(0, 0))));
    }

    [Fact]
    public void T04_plantilla_de_Jugado_con_goles_sin_autor_registrado()
    {
        var datos = Jugado(2, 1) with { GolesSinAutor = new MarcadorDelPartido(2, 1) };

        Assert.Equal(
            "Equipo Uno 2-1 Equipo Dos. Goles sin autor registrado: Equipo Uno 2, Equipo Dos 1.",
            PlantillasDeResumen.Plantilla(Entrada(datos)));
    }

    [Fact]
    public void T04_plantilla_de_Jugado_con_tarjetas_no_informadas()
    {
        var datos = Jugado(2, 1) with { GolesSinAutor = new MarcadorDelPartido(2, 1), TarjetasInformadas = false };

        Assert.Equal(
            "Equipo Uno 2-1 Equipo Dos. Goles sin autor registrado: Equipo Uno 2, Equipo Dos 1. Tarjetas no informadas.",
            PlantillasDeResumen.Plantilla(Entrada(datos)));
        Assert.Equal(
            "Equipo Uno 0-0 Equipo Dos. Tarjetas no informadas.",
            PlantillasDeResumen.Plantilla(Entrada(Jugado(0, 0) with { TarjetasInformadas = false })));
    }

    [Theory]
    [InlineData(CodigosDeTorneos.WalkOver, "W.O.")]
    [InlineData(CodigosDeTorneos.Sancion, "sanción")]
    [InlineData(CodigosDeTorneos.Otro, "decisión administrativa")]
    public void T04_plantilla_de_Adjudicado_con_cada_motivo_y_con_o_sin_marcador_deportivo(string motivo, string texto)
    {
        var sinDeportivo = DatosDePrueba.Vacio(CodigosDeTorneos.Adjudicado) with
        {
            Marcador = null, Oficial = new MarcadorDelPartido(3, 0), Motivo = motivo,
        };
        var conDeportivo = sinDeportivo with { Marcador = new MarcadorDelPartido(1, 4) };
        var esperado = $"Equipo Uno 3-0 Equipo Dos, resultado oficial por {texto}.";

        Assert.Equal(esperado, PlantillasDeResumen.Plantilla(Entrada(sinDeportivo)));
        Assert.Equal(esperado, PlantillasDeResumen.Plantilla(Entrada(conDeportivo))); // el marcador deportivo se ignora
    }

    [Fact]
    public void T04_un_Jugado_sin_marcador_y_un_Adjudicado_sin_oficial_lanzan_y_no_producen_texto()
    {
        var jugadoSinMarcador = DatosDePrueba.Vacio() with { Marcador = null };
        var adjudicadoSinOficial = DatosDePrueba.Vacio(CodigosDeTorneos.Adjudicado) with
        {
            Marcador = new MarcadorDelPartido(1, 0), Oficial = null, Motivo = CodigosDeTorneos.WalkOver,
        };

        Assert.Throws<PlanillaIncoherenteException>(() => PlantillasDeResumen.Plantilla(Entrada(jugadoSinMarcador)));
        Assert.Throws<PlanillaIncoherenteException>(() => PlantillasDeResumen.Plantilla(Entrada(adjudicadoSinOficial)));
        Assert.Throws<PlanillaIncoherenteException>(() => PlantillasDeResumen.BorradorExtendido(Entrada(jugadoSinMarcador), LongitudMaxima));
    }

    [Fact]
    public void T04_borrador_extendido_con_goles_autogol_de_local_bajo_el_visitante_y_tarjetas()
    {
        Assert.Equal(
            "Equipo Uno 1-1 Equipo Dos. Goles de Equipo Uno: Jugador Dos. "
            + "Goles de Equipo Dos: autogol de Jugador Uno (Equipo Uno). Tarjetas: Jugador Tres (Equipo Dos) amarilla.",
            PlantillasDeResumen.BorradorExtendido(Entrada(DatosDePrueba.ConAutogol()), LongitudMaxima));
    }

    [Fact]
    public void T04_borrador_extendido_con_cantidades_goles_sin_autor_y_tarjetas_de_cada_color()
    {
        var datos = Jugado(4, 3) with
        {
            Goles =
            [
                new GolDelPartido(LadoDelEquipo.Local, "Jugador Dos", "JUG-002", 2),
                new GolDelPartido(LadoDelEquipo.Local, "Jugador Tres", "JUG-003", 1),
                new GolDelPartido(LadoDelEquipo.Visitante, "Jugador Cuatro", "JUG-004", 1),
            ],
            Autogoles = [new AutogolDelPartido("Jugador Uno", "JUG-001", LadoDelEquipo.Local, 2)],
            GolesSinAutor = new MarcadorDelPartido(1, 0),
            Tarjetas =
            [
                new TarjetaDelPartido(LadoDelEquipo.Local, "Jugador Dos", "JUG-002", CodigosDeTorneos.Roja, 2),
                new TarjetaDelPartido(LadoDelEquipo.Visitante, "Jugador Cuatro", "JUG-004", CodigosDeTorneos.Azul, 1),
                new TarjetaDelPartido(LadoDelEquipo.Visitante, "Jugador Cuatro", "JUG-004", CodigosDeTorneos.Amarilla, 1),
            ],
        };

        Assert.Equal(
            "Equipo Uno 4-3 Equipo Dos. Goles de Equipo Uno: Jugador Dos (2), Jugador Tres, 1 sin autor. "
            + "Goles de Equipo Dos: Jugador Cuatro, autogol de Jugador Uno (Equipo Uno) (2). "
            + "Tarjetas: Jugador Dos (Equipo Uno) roja x2, Jugador Cuatro (Equipo Dos) amarilla, Jugador Cuatro (Equipo Dos) azul.",
            PlantillasDeResumen.BorradorExtendido(Entrada(datos), LongitudMaxima));
    }

    [Fact]
    public void T04_borrador_extendido_con_goles_solo_de_un_lado_y_sin_tarjetas_informadas()
    {
        var datos = Jugado(1, 0) with
        {
            Goles = [new GolDelPartido(LadoDelEquipo.Local, "Jugador Uno", "JUG-001", 1)],
            TarjetasInformadas = true,
        };

        Assert.Equal(
            "Equipo Uno 1-0 Equipo Dos. Goles de Equipo Uno: Jugador Uno. Sin tarjetas.",
            PlantillasDeResumen.BorradorExtendido(Entrada(datos), LongitudMaxima));
    }

    [Fact]
    public void T04_borrador_extendido_con_tarjetas_no_informadas()
    {
        var datos = Jugado(0, 1) with
        {
            Autogoles = [new AutogolDelPartido("Jugador Uno", "JUG-001", LadoDelEquipo.Local, 1)],
            TarjetasInformadas = false,
        };

        Assert.Equal(
            "Equipo Uno 0-1 Equipo Dos. Goles de Equipo Dos: autogol de Jugador Uno (Equipo Uno). Tarjetas no informadas.",
            PlantillasDeResumen.BorradorExtendido(Entrada(datos), LongitudMaxima));
    }

    [Fact]
    public void T04_un_borrador_que_pasaria_de_4000_caracteres_es_exactamente_el_compacto()
    {
        var nombre = new string('Ñ', 80);
        var datos = Jugado(40, 0) with
        {
            Goles = [.. Enumerable.Range(0, 40).Select(i => new GolDelPartido(LadoDelEquipo.Local, $"{i:D2}{nombre}"[..80], $"J-{i}", 1))],
            Tarjetas = [.. Enumerable.Range(0, 60).Select(i =>
                new TarjetaDelPartido(LadoDelEquipo.Visitante, $"{i:D2}{nombre}"[..80], $"T-{i}", CodigosDeTorneos.Amarilla, 1))],
        };
        var entrada = Entrada(datos);

        var compacto = PlantillasDeResumen.BorradorExtendido(entrada, LongitudMaxima);

        Assert.Equal("Equipo Uno 40-0 Equipo Dos. Detalle de goles y tarjetas omitido por extensión.", compacto);
        // Con una longitud máxima holgada, el mismo caso sí produce el extendido completo (la regla es la longitud).
        Assert.True(PlantillasDeResumen.BorradorExtendido(entrada, 100_000).Length > LongitudMaxima);
    }

    [Fact]
    public void T04_las_plantillas_mas_largas_con_nombres_de_80_caracteres_no_pasan_de_400()
    {
        var nombreLargo = new string('A', 80);
        var jugado = DatosDePrueba.Vacio() with
        {
            Local = nombreLargo,
            Visitante = new string('B', 80),
            Marcador = new MarcadorDelPartido(99, 99),
            GolesSinAutor = new MarcadorDelPartido(99, 99),
            TarjetasInformadas = false,
        };
        var adjudicado = DatosDePrueba.Vacio(CodigosDeTorneos.Adjudicado) with
        {
            Local = nombreLargo,
            Visitante = new string('B', 80),
            Marcador = null,
            Oficial = new MarcadorDelPartido(99, 99),
            Motivo = CodigosDeTorneos.Otro,
        };

        var textoJugado = PlantillasDeResumen.Plantilla(Entrada(jugado));
        var textoAdjudicado = PlantillasDeResumen.Plantilla(Entrada(adjudicado));
        var compacto = PlantillasDeResumen.BorradorExtendido(
            Entrada(jugado with { Goles = [new GolDelPartido(LadoDelEquipo.Local, "Jugador Uno", "J-1", 1)] }), 10);

        Assert.Equal(390, textoJugado.Length); // la más larga de R-11
        Assert.True(textoAdjudicado.Length <= 400);
        Assert.True(compacto.Length <= 400);
    }
}
