using GoalFlow.Application.Torneos.Contracts;
using static GoalFlow.Application.Torneos.Contracts.CriterioParaEstadisticas;

namespace GoalFlow.Tests.Estadisticas;

/// <summary>
/// Los fixtures F1 a F7 del Apéndice A de la Spec 009, construidos como dice el Apéndice. Los resultados esperados no están
/// aquí: cada prueba los escribe desde el Apéndice, nunca desde la salida del código. Datos inventados.
/// </summary>
public static class FixturesDelApendice
{
    // ---- Criterios y reglas del Apéndice ----

    public static readonly CriterioParaEstadisticas[] C5 =
        [PuntosDirectos, DiferenciaDeGolesDirecta, GolesAFavorDirectos, DiferenciaDeGoles, GolesAFavor];

    public static readonly CriterioParaEstadisticas[] C6 = [.. C5, FairPlay];

    public static readonly FairPlayParaEstadisticas PesosDePrueba = new(1, 2, 3);

    /// <summary>«WO-A»: valores de prueba, no un reglamento de liga.</summary>
    public static readonly AdjudicacionParaEstadisticas WoA = new(3, 0, 1, 0, 0, true, true, true, true, true);

    public static ReglamentoParaEstadisticas Reglas(
        CriterioParaEstadisticas[] criterios, bool recursivo, FairPlayParaEstadisticas? fairPlay = null,
        IReadOnlyDictionary<string, AdjudicacionParaEstadisticas>? adjudicacion = null, string estado = "Provisional", int version = 1) =>
        new(version, estado, new PuntosParaEstadisticas(3, 1, 0), new DesempateParaEstadisticas(criterios, recursivo), fairPlay, adjudicacion);

    // ---- Constructores de datos ----

    public static GolParaEstadisticas SinAutor(string equipo, int cantidad) =>
        new(TipoDeGolParaEstadisticas.SinAutor, equipo, null, cantidad);

    public static GolParaEstadisticas DeJugador(string equipo, string jugador, int cantidad) =>
        new(TipoDeGolParaEstadisticas.Jugador, equipo, jugador, cantidad);

    public static GolParaEstadisticas Autogol(string equipoAcreditado, string autor, int cantidad) =>
        new(TipoDeGolParaEstadisticas.Autogol, equipoAcreditado, autor, cantidad);

    public static TarjetaParaEstadisticas Amarillas(string jugador, int cantidad) =>
        new(jugador, ColorDeTarjetaParaEstadisticas.Amarilla, cantidad);

    /// <summary>Un partido <c>Jugado</c>; sin filas de goles explícitas, el marcador se carga como goles sin autor (Apéndice A).</summary>
    public static PartidoParaEstadisticas Jugado(
        string local, string visitante, int goles, int golesVisitante, bool informadas = true,
        IReadOnlyList<GolParaEstadisticas>? filas = null, IReadOnlyList<TarjetaParaEstadisticas>? tarjetas = null, int fase = 1)
    {
        List<GolParaEstadisticas> porDefecto = [];
        if (goles > 0) porDefecto.Add(SinAutor(local, goles));
        if (golesVisitante > 0) porDefecto.Add(SinAutor(visitante, golesVisitante));
        return new PartidoParaEstadisticas(fase, local, visitante, EstadoDePartidoParaEstadisticas.Jugado,
            new PlanillaVigenteParaEstadisticas(
                1, goles, golesVisitante, null, null, null, informadas, filas ?? porDefecto, tarjetas ?? []));
    }

    /// <summary>Un partido <c>Adjudicado</c> con marcador oficial y, si se da, marcador deportivo.</summary>
    public static PartidoParaEstadisticas Adjudicado(
        string local, string visitante, string motivo, int oficialLocal, int oficialVisitante,
        (int Local, int Visitante)? marcador = null, bool? informadas = null,
        IReadOnlyList<GolParaEstadisticas>? filas = null, IReadOnlyList<TarjetaParaEstadisticas>? tarjetas = null, int fase = 1) =>
        new(fase, local, visitante, EstadoDePartidoParaEstadisticas.Adjudicado,
            new PlanillaVigenteParaEstadisticas(
                1, marcador?.Local, marcador?.Visitante, oficialLocal, oficialVisitante, motivo, informadas, filas ?? [], tarjetas ?? []));

    public static DatosDeTorneoParaEstadisticas Torneo(
        string[] equipos, IReadOnlyList<PartidoParaEstadisticas> partidos, ReglamentoParaEstadisticas? reglamento,
        IReadOnlyList<JugadorParaEstadisticas>? jugadores = null, IReadOnlyList<EquipoParaEstadisticas>? equiposCompletos = null) =>
        new("T1", "Torneo Uno", [new FaseParaEstadisticas(1, 1)],
            equiposCompletos ?? [.. equipos.Select(e => new EquipoParaEstadisticas(e, $"Equipo {e}", true))],
            jugadores ?? [], partidos, reglamento);

    public static JugadorParaEstadisticas Jug(string codigo, string equipo) => new(codigo, $"Jugador {codigo}", equipo);

    // ---- F1 a F7 ----

    /// <summary>F1: tres equipos empatados en puntos (R1).</summary>
    public static DatosDeTorneoParaEstadisticas F1(bool recursivo) => Torneo(
        ["A", "B", "C", "D"],
        [Jugado("A", "B", 3, 0), Jugado("C", "A", 2, 1), Jugado("B", "C", 3, 1), Jugado("A", "D", 3, 0), Jugado("B", "D", 3, 0), Jugado("C", "D", 5, 0)],
        Reglas(C5, recursivo));

    /// <summary>F2: ciclo perfecto, C5 recursivo.</summary>
    public static DatosDeTorneoParaEstadisticas F2() => Torneo(
        ["A", "B", "C"], [Jugado("A", "B", 1, 0), Jugado("B", "C", 1, 0), Jugado("C", "A", 1, 0)], Reglas(C5, true));

    /// <summary>F3: fair play con datos faltantes (F3a) o completos (F3b). C6 recursivo, pesos 1, 2, 3.</summary>
    public static DatosDeTorneoParaEstadisticas F3(bool todoInformado) => Torneo(
        ["B", "C", "X", "Y"],
        [
            Jugado("B", "C", 1, 1, tarjetas: [Amarillas("PB1", 3)]),
            Jugado("B", "X", 1, 0, informadas: todoInformado),
            Jugado("B", "Y", 0, 1, informadas: todoInformado),
            Jugado("C", "X", 1, 0, tarjetas: [Amarillas("PC1", 2)]),
            Jugado("C", "Y", 0, 1, tarjetas: [Amarillas("PC1", 3)]),
            Jugado("X", "Y", 0, 0),
        ],
        Reglas(C6, true, PesosDePrueba),
        jugadores: [Jug("PB1", "B"), Jug("PC1", "C")]);

    private static IReadOnlyDictionary<string, AdjudicacionParaEstadisticas> Adj(string motivo, AdjudicacionParaEstadisticas valores) =>
        new Dictionary<string, AdjudicacionParaEstadisticas> { [motivo] = valores };

    /// <summary>F4a, F4b y F4c: walkover A–B sin marcador deportivo, criterios vacíos y no recursivo.</summary>
    public static DatosDeTorneoParaEstadisticas F4(AdjudicacionParaEstadisticas valores, int oficialA, int oficialB) => Torneo(
        ["A", "B"], [Adjudicado("A", "B", "WalkOver", oficialA, oficialB)], Reglas([], false, null, Adj("WalkOver", valores)));

    /// <summary>F5: A–B adjudicado por sanción después de un 2–2; PA1 dos goles, PB1 y PB2 uno cada uno, PA1 una amarilla.</summary>
    public static DatosDeTorneoParaEstadisticas F5(AdjudicacionParaEstadisticas valores) => Torneo(
        ["A", "B"],
        [Adjudicado("A", "B", "Sancion", 0, 3, marcador: (2, 2), informadas: true,
            filas: [DeJugador("A", "PA1", 2), DeJugador("B", "PB1", 1), DeJugador("B", "PB2", 1)],
            tarjetas: [Amarillas("PA1", 1)])],
        Reglas([], false, null, Adj("Sancion", valores)),
        jugadores: [Jug("PA1", "A"), Jug("PB1", "B"), Jug("PB2", "B")]);

    /// <summary>F6: seis equipos, quince partidos (cobertura, R5).</summary>
    public static DatosDeTorneoParaEstadisticas F6()
    {
        var jugadores = new[]
        {
            Jug("P11", "E1"), Jug("P12", "E1"), Jug("P21", "E2"), Jug("P22", "E2"), Jug("P31", "E3"), Jug("P41", "E4"),
            Jug("P51", "E5"), Jug("P61", "E6"), Jug("P62", "E6"),
        };
        PartidoParaEstadisticas Sin(string l, string v) => new(1, l, v, EstadoDePartidoParaEstadisticas.Programado, null);

        IReadOnlyList<PartidoParaEstadisticas> partidos =
        [
            Jugado("E1", "E2", 2, 1, filas: [DeJugador("E1", "P11", 2), DeJugador("E2", "P21", 1)], tarjetas: [Amarillas("P11", 1)]), // M1
            Jugado("E1", "E3", 1, 0, filas: [SinAutor("E1", 1)]),                                                                     // M2
            Jugado("E1", "E4", 0, 0, informadas: false),                                                                              // M3
            Jugado("E1", "E5", 3, 1, informadas: false,
                filas: [DeJugador("E1", "P12", 2), SinAutor("E1", 1), DeJugador("E5", "P51", 1)]),                                    // M4
            Jugado("E1", "E6", 1, 1, filas: [Autogol("E1", "P61", 1), DeJugador("E6", "P62", 1)]),                                    // M5
            Jugado("E2", "E3", 2, 2, filas: [DeJugador("E2", "P21", 2), SinAutor("E3", 2)]),                                          // M6
            Jugado("E2", "E4", 1, 0, filas: [DeJugador("E2", "P22", 1)]),                                                             // M7
            Jugado("E2", "E5", 0, 2, filas: [DeJugador("E5", "P51", 2)]),                                                             // M8
            Jugado("E2", "E6", 1, 1, filas: [DeJugador("E2", "P21", 1), DeJugador("E6", "P61", 1)]),                                  // M9
            Adjudicado("E3", "E4", "WalkOver", 3, 0),                                                                                 // M10
            new PartidoParaEstadisticas(1, "E3", "E5", EstadoDePartidoParaEstadisticas.Suspendido,                                    // M11
                new PlanillaVigenteParaEstadisticas(1, 1, 0, null, null, "Disturbios", true,
                    [DeJugador("E3", "P31", 1)], [new TarjetaParaEstadisticas("P31", ColorDeTarjetaParaEstadisticas.Roja, 1)])),
            new PartidoParaEstadisticas(1, "E3", "E6", EstadoDePartidoParaEstadisticas.Programado,                                    // M12: retirada
                new PlanillaVigenteParaEstadisticas(2, null, null, null, null, null, null, [], [])),
            Sin("E4", "E5"), Sin("E4", "E6"), Sin("E5", "E6"),                                                                        // M13 a M15
        ];
        return Torneo(["E1", "E2", "E3", "E4", "E5", "E6"], partidos, Reglas([], false, null, Adj("WalkOver", WoA)), jugadores);
    }

    /// <summary>F7: dos vueltas, B 1–0 C y C 2–0 B; criterios PuntosDirectos y DiferenciaDeGolesDirecta, recursivo.</summary>
    public static DatosDeTorneoParaEstadisticas F7() => Torneo(
        ["B", "C"], [Jugado("B", "C", 1, 0), Jugado("C", "B", 2, 0)],
        Reglas([PuntosDirectos, DiferenciaDeGolesDirecta], true));
}
