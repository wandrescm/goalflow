using GoalFlow.Application.Persistencia;
using GoalFlow.Application.Torneos.Contracts;
using GoalFlow.Domain.Torneos;
using GoalFlow.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace GoalFlow.Infrastructure.Torneos;

/// <summary>Los partidos del tenant actual con resultado vigente (Spec 007 R-34), dentro de la transacción de quien llama.</summary>
public sealed class ListadoDePartidosConResultado(GoalFlowDbContext contexto) : IListadoDePartidosConResultado
{
    public async Task<IReadOnlyList<PartidoConResultado>> ListarAsync(CancellationToken cancellationToken)
    {
        if (contexto.Database.CurrentTransaction is null) throw new TransaccionRequeridaException();

        return await contexto.Partidos.AsNoTracking()
            .Where(p => (p.Estado == EstadoDePartido.Jugado || p.Estado == EstadoDePartido.Adjudicado) && p.Version >= 1)
            .OrderBy(p => p.Id)
            .Select(p => new PartidoConResultado(p.Id, p.Version))
            .ToListAsync(cancellationToken);
    }
}
