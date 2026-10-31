namespace GoalFlow.Application.Ligas;

/// <summary>Usuario autenticado del scope. Desde un token real llega con la Spec 003.</summary>
public interface IIdentidadAutenticada
{
    Guid? UsuarioId { get; }

    void Establecer(Guid usuarioId);
}
