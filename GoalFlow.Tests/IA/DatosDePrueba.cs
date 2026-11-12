using GoalFlow.Application.Torneos.Contracts;

namespace GoalFlow.Tests.IA;

/// <summary>Planillas inventadas para las pruebas puras de IA (Spec 007 §4): nada de nombres reales.</summary>
internal static class DatosDePrueba
{
    public static readonly Guid TorneoUno = Guid.Parse("00000000-0000-0000-0000-0000000000a1");

    /// <summary>Un partido de ida sin eventos: 0-0, sin goles, sin tarjetas, tarjetas informadas.</summary>
    public static DatosDePartidoParaResumen Vacio(string estado = CodigosDeTorneos.Jugado) => new(
        TorneoId: TorneoUno,
        Version: 2,
        Estado: estado,
        Torneo: "Torneo Uno",
        Fase: 1,
        Jornada: 3,
        Local: "Equipo Uno",
        Visitante: "Equipo Dos",
        Marcador: new MarcadorDelPartido(0, 0),
        Oficial: null,
        Motivo: null,
        Goles: [],
        Autogoles: [],
        GolesSinAutor: new MarcadorDelPartido(0, 0),
        TarjetasInformadas: true,
        Tarjetas: [],
        MotivoDeRectificacion: null,
        ProgramadoPara: new DateTime(2026, 11, 14, 15, 0, 0, DateTimeKind.Utc));

    /// <summary>1-1: un gol de Jugador Dos (local) y un autogol de Jugador Uno (local) que se acredita al visitante, más una amarilla.</summary>
    public static DatosDePartidoParaResumen ConAutogol() => Vacio() with
    {
        Marcador = new MarcadorDelPartido(1, 1),
        Goles = [new GolDelPartido(LadoDelEquipo.Local, "Jugador Dos", "JUG-002", 1)],
        Autogoles = [new AutogolDelPartido("Jugador Uno", "JUG-001", LadoDelEquipo.Local, 1)],
        Tarjetas = [new TarjetaDelPartido(LadoDelEquipo.Visitante, "Jugador Tres", "JUG-003", CodigosDeTorneos.Amarilla, 1)],
    };
}
