namespace GoalFlow.Application.Ligas;

/// <summary>Rol del usuario actual en la liga actual. Exige una transacción con tenant e identidad (ADR-01 §2.2).</summary>
public interface ILectorDeMembresia
{
    Task<string?> ObtenerRolAsync(CancellationToken cancellationToken);
}
