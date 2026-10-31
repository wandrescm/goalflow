using GoalFlow.Application.Ligas;

namespace GoalFlow.Infrastructure.Ligas;

public class IdentidadAutenticada : IIdentidadAutenticada
{
    public Guid? UsuarioId { get; private set; }

    public void Establecer(Guid usuarioId) => UsuarioId = usuarioId;
}
