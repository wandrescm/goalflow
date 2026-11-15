namespace GoalFlow.Application.Torneos.Contracts;

// Contrato de lectura de Torneos para Estadísticas (Spec 009 R-30). Solo datos: sin tipos de dominio y sin identificadores
// internos, porque el cálculo puro solo puede referenciar este namespace (R-32). Todo se identifica por códigos de la liga.

public enum EstadoDePartidoParaEstadisticas
{
    Programado,
    Cancelado,
    Jugado,
    Suspendido,
    Adjudicado,
}

public enum TipoDeGolParaEstadisticas
{
    Jugador,
    Autogol,
    SinAutor,
}

public enum ColorDeTarjetaParaEstadisticas
{
    Amarilla,
    Azul,
    Roja,
}

public enum CriterioParaEstadisticas
{
    PuntosDirectos,
    DiferenciaDeGolesDirecta,
    GolesAFavorDirectos,
    DiferenciaDeGoles,
    GolesAFavor,
    FairPlay,
}

public sealed record FaseParaEstadisticas(int Orden, int Vueltas);

/// <param name="Inscrito"><c>false</c> si la participación está <c>Retirado</c>.</param>
public sealed record EquipoParaEstadisticas(string Codigo, string Nombre, bool Inscrito);

/// <param name="EquipoCodigo">El equipo del jugador en este torneo.</param>
public sealed record JugadorParaEstadisticas(string Codigo, string Nombre, string EquipoCodigo);

/// <param name="EquipoAcreditadoCodigo">El equipo al que se acredita el gol; en un autogol es el rival del autor.</param>
/// <param name="JugadorCodigo">Nulo si y solo si el tipo es <c>SinAutor</c>.</param>
public sealed record GolParaEstadisticas(TipoDeGolParaEstadisticas Tipo, string EquipoAcreditadoCodigo, string? JugadorCodigo, int Cantidad);

public sealed record TarjetaParaEstadisticas(string JugadorCodigo, ColorDeTarjetaParaEstadisticas Color, int Cantidad);

/// <summary>La planilla vigente de un partido, con sus filas de goles y de tarjetas. Nunca mezcla versiones (R-11).</summary>
public sealed record PlanillaVigenteParaEstadisticas(
    int Version,
    int? MarcadorLocal,
    int? MarcadorVisitante,
    int? OficialLocal,
    int? OficialVisitante,
    string? Motivo,
    bool? TarjetasInformadas,
    IReadOnlyList<GolParaEstadisticas> Goles,
    IReadOnlyList<TarjetaParaEstadisticas> Tarjetas);

/// <param name="Estado">El estado vigente del partido; <c>Programado</c> si la planilla vigente es un retiro.</param>
/// <param name="Planilla">Nula si el partido nunca tuvo una planilla (versión 0).</param>
public sealed record PartidoParaEstadisticas(
    int FaseOrden,
    string LocalCodigo,
    string VisitanteCodigo,
    EstadoDePartidoParaEstadisticas Estado,
    PlanillaVigenteParaEstadisticas? Planilla);

public sealed record PuntosParaEstadisticas(int Victoria, int Empate, int Derrota);

public sealed record DesempateParaEstadisticas(IReadOnlyList<CriterioParaEstadisticas> Criterios, bool DirectoRecursivo);

public sealed record FairPlayParaEstadisticas(int Amarilla, int Azul, int Roja);

public sealed record AdjudicacionParaEstadisticas(
    int PuntosGanador, int PuntosPerdedor, int PuntosEmpate, int DeduccionPerdedor, int DeduccionEmpate,
    bool CuentaComoJugado, bool GolesOficialesEnTabla, bool GolesOficialesEnValla,
    bool GolesDeportivosEnGoleadores, bool TarjetasEnFairPlay);

/// <summary>El reglamento vigente. Una sección nula es «no decidida» (Spec 009 C-13); nunca se le pone un valor por defecto.</summary>
/// <param name="Estado"><c>Provisional</c> o <c>Confirmado</c>; no cambia ningún cálculo (R-36).</param>
/// <param name="Adjudicacion">Por motivo (<c>WalkOver</c>, <c>Sancion</c>, <c>Otro</c>); nula si la sección está ausente.</param>
public sealed record ReglamentoParaEstadisticas(
    int Version,
    string Estado,
    PuntosParaEstadisticas? Puntos,
    DesempateParaEstadisticas? Desempate,
    FairPlayParaEstadisticas? FairPlay,
    IReadOnlyDictionary<string, AdjudicacionParaEstadisticas>? Adjudicacion);

public sealed record DatosDeTorneoParaEstadisticas(
    string Codigo,
    string Nombre,
    IReadOnlyList<FaseParaEstadisticas> Fases,
    IReadOnlyList<EquipoParaEstadisticas> Equipos,
    IReadOnlyList<JugadorParaEstadisticas> Jugadores,
    IReadOnlyList<PartidoParaEstadisticas> Partidos,
    ReglamentoParaEstadisticas? Reglamento);

/// <summary>
/// Lee, dentro de la transacción de quien llama, el torneo del tenant actual con todo lo que necesitan las estadísticas
/// (Spec 009 R-30). Corre un número fijo de comandos, sin importar cuántos partidos, equipos o jugadores haya (R-29).
/// </summary>
public interface ILectorDeTorneoParaEstadisticas
{
    /// <returns>Nulo si el torneo no existe en el tenant actual.</returns>
    Task<DatosDeTorneoParaEstadisticas?> LeerAsync(string codigoDeTorneo, CancellationToken cancellationToken);
}
