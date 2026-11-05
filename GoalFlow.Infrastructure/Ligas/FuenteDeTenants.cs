using GoalFlow.Application.Mensajeria;
using GoalFlow.Domain.Tenancy;
using GoalFlow.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace GoalFlow.Infrastructure.Ligas;

/// <summary>
/// Lee solo los identificadores de <c>ligas</c>, sin transacción ni contexto, como el resolutor de slug
/// (ADR-14 §2.3, segunda excepción; Spec 004 R-04).
/// </summary>
public class FuenteDeTenants(GoalFlowDbContext contexto) : IFuenteDeTenants
{
    public async Task<IReadOnlyList<TenantId>> ListarAsync(CancellationToken cancellationToken) =>
        await contexto.Ligas.AsNoTracking().OrderBy(l => l.Id).Select(l => l.Id).ToListAsync(cancellationToken);
}
