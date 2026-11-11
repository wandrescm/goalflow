namespace GoalFlow.Application.Torneos.Contracts;

/// <summary>
/// Lee, dentro de la transacción de quien llama, la planilla vigente de un partido del tenant actual resuelta a nombres
/// (Spec 007 R-33). Solo lee ese partido, su planilla vigente con sus filas y los nombres que intervienen.
/// </summary>
public interface ILectorDePartidoParaResumen
{
    /// <summary>Null si el partido no existe en el tenant actual.</summary>
    Task<DatosDePartidoParaResumen?> LeerAsync(Guid partidoId, CancellationToken cancellationToken);
}

/// <summary>Un partido con resultado y su versión vigente.</summary>
public sealed record PartidoConResultado(Guid PartidoId, int Version);

/// <summary>
/// Lista, dentro de la transacción de quien llama, los partidos del tenant actual cuyo estado vigente es <c>Jugado</c> o
/// <c>Adjudicado</c> y cuya versión es al menos 1, ordenados por identificador (Spec 007 R-34).
/// </summary>
public interface IListadoDePartidosConResultado
{
    Task<IReadOnlyList<PartidoConResultado>> ListarAsync(CancellationToken cancellationToken);
}
