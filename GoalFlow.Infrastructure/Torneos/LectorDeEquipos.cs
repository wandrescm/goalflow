using GoalFlow.Application.Torneos;
using GoalFlow.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace GoalFlow.Infrastructure.Torneos;

public class LectorDeEquipos(GoalFlowDbContext contexto) : ILectorDeEquipos
{
    public async Task<IReadOnlyList<EquipoResumen>> ListarAsync(CancellationToken cancellationToken) =>
        await contexto.Equipos
            .OrderBy(e => e.Nombre)
            .Select(e => new EquipoResumen(e.Id, e.Nombre))
            .ToListAsync(cancellationToken);
}
