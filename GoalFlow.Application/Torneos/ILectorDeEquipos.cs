namespace GoalFlow.Application.Torneos;

public record EquipoResumen(Guid Id, string Nombre);

/// <summary>Lectura de equipos; corre dentro de una transacción de la unidad de trabajo.</summary>
public interface ILectorDeEquipos
{
    Task<IReadOnlyList<EquipoResumen>> ListarAsync(CancellationToken cancellationToken);
}
