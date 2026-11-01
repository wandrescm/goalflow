using GoalFlow.Application.Ligas;
using GoalFlow.Domain.Ligas;
using GoalFlow.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace GoalFlow.Infrastructure.Ligas;

/// <summary>Lee ligas sin transacción: aún no hay tenant ni identidad (ADR-14 §2.3).</summary>
public class ResolutorDeSlug(GoalFlowDbContext contexto) : IResolutorDeSlug
{
    public Task<Liga?> ResolverAsync(string slug, CancellationToken cancellationToken) =>
        contexto.Ligas.AsNoTracking().FirstOrDefaultAsync(l => l.Slug == slug, cancellationToken);
}
