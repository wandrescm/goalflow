using GoalFlow.Application.IA;
using GoalFlow.Application.Persistencia;
using GoalFlow.Domain.IA;
using GoalFlow.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace GoalFlow.Infrastructure.IA;

/// <summary>Contador de presupuesto sobre EF Core: sin SQL crudo, con <c>ExecuteUpdateAsync</c> condicionado (Spec 007 R-19, R-26).</summary>
public sealed class AlmacenDePresupuesto(GoalFlowDbContext contexto) : IAlmacenDePresupuesto
{
    public async Task AsegurarContadorAsync(string mes, long topePorLiga, CancellationToken cancellationToken)
    {
        ExigirTransaccion();
        if (await contexto.PresupuestosIa.AnyAsync(c => c.Mes == mes, cancellationToken)) return;

        // El tenant lo asigna el interceptor de guardado (default = el tenant del scope).
        var contador = new PresupuestoIa(Guid.CreateVersion7(), default, mes, topePorLiga);
        contexto.PresupuestosIa.Add(contador);
        try
        {
            await contexto.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ErroresDeBase.EsUnicidadDe(ex, ConfiguracionDeIa.IndiceDeContadorPorMes))
        {
            // Otro trabajador creó el contador del mes a la vez (R-19 a): se usa el suyo. La transacción sigue usable
            // (savepoint automático), pero la fila fallida debe salir del rastreador o el siguiente guardado la reintentaría.
            contexto.Entry(contador).State = EntityState.Detached;
        }
    }

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
