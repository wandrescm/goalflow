using GoalFlow.Application.Ligas;
using GoalFlow.Application.Persistencia;
using GoalFlow.Application.Tenancy;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoalFlow.Infrastructure.Persistencia;

public class UnidadDeTrabajo(
    GoalFlowDbContext contexto, ITenantContext tenantContext, IIdentidadAutenticada identidad) : IUnidadDeTrabajo
{
    public async Task<ITransaccion> IniciarAsync(CancellationToken cancellationToken)
    {
        // Antes de usar EF Core: sin contexto, la conexión nunca se abre (Spec 002 R-23).
        InterceptorDeTransacciones.ExigirContexto(tenantContext, identidad);

        return new Transaccion(await contexto.Database.BeginTransactionAsync(cancellationToken));
    }

    private sealed class Transaccion(IDbContextTransaction transaccion) : ITransaccion
    {
        public Task ConfirmarAsync(CancellationToken cancellationToken) => transaccion.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync() => transaccion.DisposeAsync();
    }
}
