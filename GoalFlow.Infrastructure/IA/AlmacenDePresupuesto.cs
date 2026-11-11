using GoalFlow.Application.IA;
using GoalFlow.Application.Persistencia;
using GoalFlow.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace GoalFlow.Infrastructure.IA;

/// <summary>Contador de presupuesto sobre EF Core: sin SQL crudo, con <c>ExecuteUpdateAsync</c> condicionado (Spec 007 R-19, R-26).</summary>
public sealed class AlmacenDePresupuesto(GoalFlowDbContext contexto) : IAlmacenDePresupuesto
{
    public async Task<bool> ReservarAsync(string mes, long monto, CancellationToken cancellationToken)
    {
        ExigirTransaccion();

        var afectadas = await contexto.PresupuestosIa
            .Where(c => c.Mes == mes && c.Reservado + c.Consumido + monto <= c.Tope)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Reservado, c => c.Reservado + monto), cancellationToken);

        return afectadas == 1;
    }

    private void ExigirTransaccion()
    {
        if (contexto.Database.CurrentTransaction is null)
            throw new TransaccionRequeridaException();
    }
}
