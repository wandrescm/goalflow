using GoalFlow.Application.Persistencia;
using GoalFlow.Application.Tenancy;
using GoalFlow.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;

namespace GoalFlow.Infrastructure.Persistencia;

/// <summary>Exige transacción explícita y protege el TenantId al guardar (ADR-14 §2.3 y §2.5).</summary>
public class InterceptorDeGuardado(ITenantContext tenantContext) : SaveChangesInterceptor
{
    private const string PropiedadTenant = nameof(TenantId);

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Validar(eventData.Context!);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Validar(eventData.Context!);
        return ValueTask.FromResult(result);
    }

    private void Validar(DbContext contexto)
    {
        if (contexto.Database.CurrentTransaction is null)
            throw new TransaccionRequeridaException();

        // Este hook corre antes de la detección de cambios de SaveChanges.
        contexto.ChangeTracker.DetectChanges();

        foreach (var entrada in contexto.ChangeTracker.Entries())
        {
            if (entrada.Metadata.FindDeclaredQueryFilter(GoalFlowDbContext.FiltroTenant) is null) continue;

            var propiedad = entrada.Property(PropiedadTenant);
            var tenant = (TenantId)propiedad.CurrentValue!;

            if (entrada.State == EntityState.Added)
            {
                if (tenant.EsVacio) propiedad.CurrentValue = tenantContext.Current;
                else if (tenant != tenantContext.Current) throw new TenantInvalidoException(entrada.Metadata.ClrType.Name);
            }
            else if (entrada.State == EntityState.Modified && tenant != (TenantId)propiedad.OriginalValue!)
            {
                throw new TenantInvalidoException(entrada.Metadata.ClrType.Name);
            }
        }
    }
}
