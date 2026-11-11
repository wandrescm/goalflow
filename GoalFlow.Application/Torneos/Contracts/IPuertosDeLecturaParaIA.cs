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
