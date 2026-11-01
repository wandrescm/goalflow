using GoalFlow.Application.Ligas;
using GoalFlow.Application.Tenancy;
using GoalFlow.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace GoalFlow.Infrastructure.Ligas;

public class LectorDeMembresia(GoalFlowDbContext contexto, ITenantContext tenantContext) : ILectorDeMembresia
{
    // admins_liga no tiene filtro Tenant y su RLS solo restringe por usuario: la liga se filtra aquí.
    public Task<string?> ObtenerRolAsync(CancellationToken cancellationToken)
    {
        var tenant = tenantContext.Current;

        return contexto.AdminsLiga
            .Where(a => a.TenantId == tenant)
            .Select(a => a.Rol)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
