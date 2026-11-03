using GoalFlow.Application.Ligas;
using GoalFlow.Application.Tenancy;
using GoalFlow.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace GoalFlow.Infrastructure.Ligas;

public class LectorDeMembresia(
    GoalFlowDbContext contexto, ITenantContext tenantContext, IIdentidadAutenticada identidad) : ILectorDeMembresia
{
    // admins_liga no tiene filtro Tenant. Liga y usuario se filtran aquí; RLS es la segunda capa (ADR-01 §2.2).
    public Task<string?> ObtenerRolAsync(CancellationToken cancellationToken)
    {
        var tenant = tenantContext.Current;
        var usuario = identidad.UsuarioId;

        return contexto.AdminsLiga
            .Where(a => a.TenantId == tenant && a.UsuarioId == usuario)
            .Select(a => a.Rol)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
