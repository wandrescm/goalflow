namespace GoalFlow.Application.Torneos.Contracts;

/// <summary>El lado de un equipo en un partido.</summary>
public enum LadoDelEquipo
{
    Local,
    Visitante,
}

/// <summary>Un marcador: goles del local y del visitante.</summary>
public sealed record MarcadorDelPartido(int Local, int Visitante);

/// <summary>Goles de jugador acreditados al equipo <see cref="Equipo"/>, que es también el lado del autor.</summary>
public sealed record GolDelPartido(LadoDelEquipo Equipo, string Jugador, string CodigoDeInscripcion, int Cantidad);

/// <summary>Autogoles de <see cref="Jugador"/>, que juega en <see cref="EquipoDelAutor"/>; se acreditan al otro lado.</summary>
public sealed record AutogolDelPartido(string Jugador, string CodigoDeInscripcion, LadoDelEquipo EquipoDelAutor, int Cantidad);

public sealed record TarjetaDelPartido(LadoDelEquipo Equipo, string Jugador, string CodigoDeInscripcion, string Color, int Cantidad);

/// <summary>
/// Los datos de la planilla vigente de un partido, resueltos a nombres (Spec 007 R-33). Los códigos de inscripción, la
/// versión, el motivo de rectificación y la hora programada se leen, pero la entrada canónica nunca los emite (R-08, R-09):
/// están aquí para que esa regla sea comprobable. Los estados y motivos son los códigos de Torneos (<c>Jugado</c>,
/// <c>WalkOver</c>, ...), porque IA solo depende de <c>Torneos.Contracts</c> (R-35).
/// </summary>
public sealed record DatosDePartidoParaResumen(
    Guid TorneoId,
    int Version,
    string Estado,
    string Torneo,
    int Fase,
    int Jornada,
    string Local,
    string Visitante,
    MarcadorDelPartido? Marcador,
    MarcadorDelPartido? Oficial,
    string? Motivo,
    IReadOnlyList<GolDelPartido> Goles,
    IReadOnlyList<AutogolDelPartido> Autogoles,
    MarcadorDelPartido GolesSinAutor,
    bool? TarjetasInformadas,
    IReadOnlyList<TarjetaDelPartido> Tarjetas,
    string? MotivoDeRectificacion,
    DateTime? ProgramadoPara);

/// <summary>Los códigos de estado, motivo y color que IA entiende (los mismos que guarda Torneos).</summary>
public static class CodigosDeTorneos
{
    public const string Programado = "Programado";
    public const string Cancelado = "Cancelado";
    public const string Jugado = "Jugado";
    public const string Suspendido = "Suspendido";
    public const string Adjudicado = "Adjudicado";

    public const string WalkOver = "WalkOver";
    public const string Sancion = "Sancion";
    public const string Otro = "Otro";

    public const string Amarilla = "Amarilla";
    public const string Azul = "Azul";
    public const string Roja = "Roja";
}
