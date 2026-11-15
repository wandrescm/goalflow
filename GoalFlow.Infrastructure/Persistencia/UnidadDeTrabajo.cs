using GoalFlow.Application.Ligas;
using GoalFlow.Application.Persistencia;
using GoalFlow.Application.Tenancy;
using Microsoft.EntityFrameworkCore;
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

    public async Task<ITransaccion> IniciarLecturaConsistenteAsync(CancellationToken cancellationToken)
    {
        InterceptorDeTransacciones.ExigirContexto(tenantContext, identidad);

        // La sobrecarga de nivel de aislamiento de EF Core, sin SQL crudo (Spec 009 R-33). La foto de REPEATABLE READ la toma
        // la primera sentencia de la transacción, el set_config del interceptor, antes de cualquier lectura del puerto.
        return new Transaccion(await contexto.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, cancellationToken));
    }

    private sealed class Transaccion(IDbContextTransaction transaccion) : ITransaccion
    {
        public Task ConfirmarAsync(CancellationToken cancellationToken) => transaccion.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync() => transaccion.DisposeAsync();
    }
}
