using GoalFlow.Application.IA;
using GoalFlow.Application.Torneos.Contracts;

namespace GoalFlow.Tests.IA.Verificacion;

/// <summary>La etiqueta del propietario (Spec 008 R-19). <c>Ambiguo</c> cuenta como "no debe publicarse".</summary>
internal enum Etiqueta
{
    Fiel,
    Ambiguo,
    NoFiel,
}

/// <summary>De dónde sale un texto del corpus (Spec 008 R-19).</summary>
internal enum Corrida
{
    /// <summary>La medición del 2026-11-12 (M1 a M9).</summary>
    Medicion,

    /// <summary>La confirmación del 2026-11-12 (C1 a C5).</summary>
    Confirmacion,

    /// <summary>Una mutación adversarial del Apéndice A.2.</summary>
    Mutacion,
}

/// <summary>
/// Un texto del corpus con su veredicto esperado bajo <c>v1</c>: aprobado si <paramref name="ReglasEsperadas"/> está vacía.
/// <paramref name="FalsoNegativoFijado"/> marca un límite conocido (a4, a8) que se conserva tal como es.
/// </summary>
internal sealed record CasoDelCorpus(
    string Id,
    Corrida Corrida,
    string Ajuste,
    string Fixture,
    string Texto,
    Etiqueta Etiqueta,
    string[] ReglasEsperadas,
    bool FalsoNegativoFijado = false)
{
    public bool AprobadoEsperado => ReglasEsperadas.Length == 0;

    public override string ToString() => Id;
}

/// <summary>
/// El corpus <c>corpus-v1</c> de la Spec 008 (Apéndice A): los 14 textos reales y las 12 mutaciones, con sus fixtures F1 a F5.
/// Los textos están copiados literalmente del Apéndice; los veredictos son los del Apéndice y no se ajustan para que pase una prueba.
/// </summary>
internal static class CorpusV1
{
    private static readonly Guid TorneoUno = Guid.Parse("00000000-0000-0000-0000-0000000000a1");

    // ---- A. Fixtures (Spec 007 T-21 b) ----

    private static DatosDePartidoParaResumen Base(
        MarcadorDelPartido marcador, GolDelPartido[] goles, AutogolDelPartido[] autogoles, MarcadorDelPartido golesSinAutor,
        bool tarjetasInformadas, TarjetaDelPartido[] tarjetas) => new(
        TorneoId: TorneoUno,
        Version: 1,
        Estado: CodigosDeTorneos.Jugado,
        Torneo: "Torneo Uno",
        Fase: 1,
        Jornada: 3,
        Local: "Equipo Uno",
        Visitante: "Equipo Dos",
        Marcador: marcador,
        Oficial: null,
        Motivo: null,
        Goles: goles,
        Autogoles: autogoles,
        GolesSinAutor: golesSinAutor,
        TarjetasInformadas: tarjetasInformadas,
        Tarjetas: tarjetas,
        MotivoDeRectificacion: null,
        ProgramadoPara: new DateTime(2026, 11, 14, 15, 0, 0, DateTimeKind.Utc));

    private static GolDelPartido Gol(LadoDelEquipo lado, string jugador, int cantidad) => new(lado, jugador, "INS-" + jugador, cantidad);

    private static TarjetaDelPartido Tarjeta(LadoDelEquipo lado, string jugador, string color, int cantidad) =>
        new(lado, jugador, "INS-" + jugador, color, cantidad);

    private static readonly Dictionary<string, DatosDePartidoParaResumen> Fixtures = new()
    {
        // F1: 2-1; goles de Jugador Uno, Jugador Dos y Jugador Tres; Jugador Cuatro amarilla ×1; tarjetas informadas. A = {0, 1, 2, 3}.
        ["F1"] = Base(
            new(2, 1),
            [Gol(LadoDelEquipo.Local, "Jugador Uno", 1), Gol(LadoDelEquipo.Local, "Jugador Dos", 1), Gol(LadoDelEquipo.Visitante, "Jugador Tres", 1)],
            [], new(0, 0), true,
            [Tarjeta(LadoDelEquipo.Visitante, "Jugador Cuatro", CodigosDeTorneos.Amarilla, 1)]),

        // F2: 0-1; autogol de Jugador Uno ×1; Jugador Dos roja ×1; tarjetas informadas. A = {0, 1, 3}.
        ["F2"] = Base(
            new(0, 1),
            [],
            [new AutogolDelPartido("Jugador Uno", "INS-Jugador Uno", LadoDelEquipo.Local, 1)],
            new(0, 0), true,
            [Tarjeta(LadoDelEquipo.Local, "Jugador Dos", CodigosDeTorneos.Roja, 1)]),

        // F3: 3-3; Jugador Uno 2, Jugador Dos 1, Jugador Tres 2, Jugador Cuatro 1; tarjetas NO informadas. A = {0, 1, 2, 3, 6}.
        ["F3"] = Base(
            new(3, 3),
            [
                Gol(LadoDelEquipo.Local, "Jugador Uno", 2), Gol(LadoDelEquipo.Local, "Jugador Dos", 1),
                Gol(LadoDelEquipo.Visitante, "Jugador Tres", 2), Gol(LadoDelEquipo.Visitante, "Jugador Cuatro", 1),
            ],
            [], new(0, 0), false, []),

        // F4: 2-0; Jugador Dos 1; sin autor 1-0; tarjetas informadas, ninguna. A = {0, 1, 2, 3}.
        ["F4"] = Base(
            new(2, 0),
            [Gol(LadoDelEquipo.Local, "Jugador Dos", 1)],
            [], new(1, 0), true, []),

        // F5: 0-0; Jugador Uno amarilla ×2, Jugador Tres azul ×1, Jugador Cuatro roja ×1; tarjetas informadas. A = {0, 1, 2, 3, 4}.
        ["F5"] = Base(
            new(0, 0),
            [], [], new(0, 0), true,
            [
                Tarjeta(LadoDelEquipo.Local, "Jugador Uno", CodigosDeTorneos.Amarilla, 2),
                Tarjeta(LadoDelEquipo.Visitante, "Jugador Tres", CodigosDeTorneos.Azul, 1),
                Tarjeta(LadoDelEquipo.Visitante, "Jugador Cuatro", CodigosDeTorneos.Roja, 1),
            ]),
    };

    public static DatosDePartidoParaResumen Datos(string fixture) => Fixtures[fixture];

    public static EntradaCanonica Entrada(string fixture) => EntradaCanonica.Construir(Fixtures[fixture]);

    // ---- A.1. Los 14 textos reales (verbatim) ----

    public static readonly IReadOnlyList<CasoDelCorpus> Reales =
    [
        new("M1", Corrida.Medicion, "Deshabilitado", "F1",
            "En la jornada 3 de la fase 1 del Torneo Uno, Equipo Uno se impuso 2-1 a Equipo Dos. Por el local marcaron Jugador Dos y Jugador Uno, mientras que Jugador Tres anotó el gol del visitante. Jugador Cuatro, de Equipo Dos, recibió una tarjeta amarilla. El partido figura con estado Jugado.",
            Etiqueta.Fiel, []),
        new("M2", Corrida.Medicion, "Deshabilitado", "F2",
            "En la jornada 3 de la fase 1 del Torneo Uno, Equipo Uno cayó ante Equipo Dos por 0 a 1. El único tanto del encuentro fue un autogol de Jugador Uno, de Equipo Uno, que benefició a Equipo Dos. Además, Jugador Dos, de Equipo Uno, recibió una tarjeta roja.",
            Etiqueta.Fiel, []),
        new("M3", Corrida.Medicion, "Deshabilitado", "F3",
            "En la jornada 3 de la fase 1 del Torneo Uno, Equipo Uno y Equipo Dos igualaron 3 a 3. Por el Equipo Uno, Jugador Uno anotó 2 goles y Jugador Dos marcó 1. Por el Equipo Dos, Jugador Tres anotó 2 goles y Jugador Cuatro marcó 1. No se informaron autogoles ni tarjetas en este partido.",
            Etiqueta.Fiel, []),
        new("M4", Corrida.Medicion, "Deshabilitado", "F4",
            "En la jornada 3 de la fase 1 del Torneo Uno, Equipo Uno venció 2-0 a Equipo Dos. Jugador Dos anotó un gol para el local, y se registró además un gol sin autor atribuido al equipo local. No hubo autogoles ni tarjetas informadas en el partido, y el encuentro quedó marcado como jugado.",
            Etiqueta.Ambiguo, ["V6"]),
        new("M5", Corrida.Medicion, "Deshabilitado", "F5",
            "En la jornada 3 de la fase 1 del Torneo Uno, Equipo Uno y Equipo Dos empataron 0 a 0 en un partido jugado. No se registraron goles ni autogoles. Se informaron tarjetas: Jugador Uno de Equipo Uno recibió 2 amarillas, Jugador Cuatro de Equipo Dos vio 1 roja y Jugador Tres de Equipo Dos recibió 1 tarjeta azul.",
            Etiqueta.Fiel, []),
        new("M6", Corrida.Medicion, "Adaptativo", "F1",
            "En la jornada 3 del Torneo Uno, Equipo Uno se impuso 2-1 a Equipo Dos. Por el local marcaron Jugador Dos y Jugador Uno, mientras que Jugador Tres descontó para el visitante. Jugador Cuatro recibió una tarjeta amarilla por Equipo Dos. El partido de la fase 1 quedó registrado como jugado y sin incidencias oficiales.",
            Etiqueta.NoFiel, ["V3", "V4"]),
        new("M7", Corrida.Medicion, "Adaptativo", "F2",
            "En la jornada 3 del Torneo Uno, Equipo Uno cayó 0-1 ante Equipo Dos. El único gol del encuentro fue un autogol de Jugador Uno, de Equipo Uno, que benefició al visitante. Además, Jugador Dos, de Equipo Uno, recibió una tarjeta roja.",
            Etiqueta.Fiel, []),
        new("M8", Corrida.Medicion, "Adaptativo", "F3",
            "En la jornada 3 de la fase 1 del Torneo Uno, Equipo Uno y Equipo Dos igualaron 3 a 3. Equipo Uno se adelantó con goles de Jugador Dos (1) y Jugador Uno (2), mientras que Equipo Dos respondió con tanto de Jugador Cuatro (1) y dos goles de Jugador Tres (2). El partido quedó registrado como jugado, sin autogoles ni tarjetas informadas.",
            Etiqueta.NoFiel, ["V3"]),
        new("M9", Corrida.Medicion, "Adaptativo", "F4",
            "En la jornada 3 de la fase 1 del Torneo Uno, Equipo Uno se impuso 2-0 como local ante Equipo Dos. El partido figura como jugado. Jugador Dos anotó un gol para el local, y Equipo Uno registró además un gol sin autor informado. No hubo autogoles ni tarjetas informadas para este encuentro.",
            Etiqueta.Ambiguo, ["V6"]),
        new("C1", Corrida.Confirmacion, "Deshabilitado", "F1",
            "En la jornada 3 de la fase 1 del Torneo Uno, Equipo Uno se impuso 2-1 a Equipo Dos. Por el local marcaron Jugador Dos y Jugador Uno, y por el visitante anotó Jugador Tres. Jugador Cuatro, de Equipo Dos, recibió una tarjeta amarilla. El partido fue jugado y no hubo autogoles.",
            Etiqueta.Fiel, []),
        new("C2", Corrida.Confirmacion, "Deshabilitado", "F2",
            "En la jornada 3 del Torneo Uno, Equipo Uno cayó ante Equipo Dos por 0 a 1. El único tanto del encuentro llegó por un autogol de Jugador Uno, de Equipo Uno, que benefició a Equipo Dos. Además, Jugador Dos, de Equipo Uno, recibió una tarjeta roja.",
            Etiqueta.Fiel, []),
        new("C3", Corrida.Confirmacion, "Deshabilitado", "F3",
            "En la jornada 3 de la fase 1 del Torneo Uno, Equipo Uno y Equipo Dos igualaron 3 a 3. Por Equipo Uno, Jugador Uno marcó 2 goles y Jugador Dos anotó 1. Por Equipo Dos, Jugador Tres marcó 2 goles y Jugador Cuatro anotó 1. No se informaron autogoles ni tarjetas en este partido.",
            Etiqueta.Fiel, []),
        new("C4", Corrida.Confirmacion, "Deshabilitado", "F4",
            "En la jornada 3 de la fase 1 del Torneo Uno, Equipo Uno venció 2-0 a Equipo Dos. El partido quedó registrado como jugado. Jugador Dos anotó un gol para el local, y Equipo Uno sumó además un gol sin autor informado. No se registraron autogoles ni tarjetas en el partido.",
            Etiqueta.Fiel, []),
        new("C5", Corrida.Confirmacion, "Deshabilitado", "F5",
            "En la jornada 3 de la fase 1 del Torneo Uno, Equipo Uno y Equipo Dos igualaron 0 a 0. No se registraron goles ni autogoles. Las tarjetas informadas fueron dos amarillas para Jugador Uno del Equipo Uno, una roja para Jugador Cuatro del Equipo Dos y una azul para Jugador Tres del Equipo Dos.",
            Etiqueta.Fiel, []),
    ];

    // ---- A.2. Las 12 mutaciones adversariales ----

    private static CasoDelCorpus Real(string id) => Reales.Single(c => c.Id == id);

    // El cambio se aplica sobre el texto base del Apéndice y debe encontrar su fragmento exactamente una vez.
    private static CasoDelCorpus Mutacion(
        string id, string baseId, string? de, string? a, string? agregar, Etiqueta etiqueta, string[] reglas, bool fijado = false)
    {
        var origen = Real(baseId);
        var texto = origen.Texto;
        if (de is not null)
        {
            var primero = texto.IndexOf(de, StringComparison.Ordinal);
            if (primero < 0 || texto.IndexOf(de, primero + 1, StringComparison.Ordinal) >= 0)
                throw new InvalidOperationException($"{id}: el fragmento '{de}' debe aparecer exactamente una vez en {baseId}.");
            texto = texto.Replace(de, a, StringComparison.Ordinal);
        }

        if (agregar is not null) texto += agregar;
        return new CasoDelCorpus(id, Corrida.Mutacion, "n/a", origen.Fixture, texto, etiqueta, reglas, fijado);
    }

    public static readonly IReadOnlyList<CasoDelCorpus> Mutaciones =
    [
        Mutacion("a1", "C1", "anotó Jugador Tres.", "anotó Jugador Tres al minuto 30.", null, Etiqueta.NoFiel, ["V2", "V3"]),
        Mutacion("a2", "C1", "Jugador Tres", "Jugador Siete", null, Etiqueta.NoFiel, ["V1", "V2"]),
        Mutacion("a3", "C1", "2-1", "4-1", null, Etiqueta.NoFiel, ["V2"]),
        Mutacion("a4", "C1", "2-1", "3-1", null, Etiqueta.NoFiel, [], fijado: true),
        Mutacion("a5", "C1", "tarjeta amarilla", "tarjeta roja", null, Etiqueta.NoFiel, ["V7"]),
        Mutacion("a6", "C1", null, null, " El árbitro anuló un gol.", Etiqueta.NoFiel, ["V4"]),
        Mutacion("a7", "C1", null, null, " Hubo lluvia en la cancha.", Etiqueta.NoFiel, ["V5"]),
        Mutacion("a8", "C1", "por el visitante anotó Jugador Tres", "por el visitante anotó Jugador Cuatro", null, Etiqueta.NoFiel, [], fijado: true),
        Mutacion("a9", "C4", "No se registraron autogoles ni tarjetas", "No se informaron autogoles ni tarjetas", null, Etiqueta.Ambiguo, ["V6"]),
        Mutacion("a10", "C3", "No se informaron autogoles ni tarjetas en este partido.", "No hubo autogoles ni tarjetas en este partido.", null, Etiqueta.NoFiel, ["V6"]),
        Mutacion("a11", "C1", "Jugador Cuatro, de Equipo Dos,", "Cuatro, de Equipo Dos,", null, Etiqueta.Fiel, ["V1", "V2"]),
        Mutacion("a12", "C2", null, null, " Hubo calma luego.", Etiqueta.NoFiel, ["V3"]),
    ];

    public static CasoDelCorpus Caso(string id) =>
        Reales.Concat(Mutaciones).Single(c => c.Id == id);

    public static IEnumerable<object[]> IdsDeLosTextosReales() => Reales.Select(c => new object[] { c.Id });

    public static IEnumerable<object[]> IdsDeLasMutaciones() => Mutaciones.Select(c => new object[] { c.Id });
}
