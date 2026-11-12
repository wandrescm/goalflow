using GoalFlow.Application.IA;
using GoalFlow.Application.Persistencia;
using GoalFlow.Domain.IA;
using GoalFlow.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace GoalFlow.Infrastructure.IA;

/// <summary>Los resúmenes sobre EF Core: solo SELECT e INSERT (Spec 007 R-04). Dentro de la transacción de la unidad de trabajo.</summary>
public sealed class AlmacenDeResumenes(GoalFlowDbContext contexto) : IAlmacenDeResumenes
{
    public async Task<bool> ExisteAsync(Guid partidoId, string huella, CancellationToken cancellationToken)
    {
        ExigirTransaccion();
        return await contexto.ResumenesPartido.AnyAsync(r => r.PartidoId == partidoId && r.Huella == huella, cancellationToken);
    }

    public async Task<bool> InsertarAsync(ResumenPartido resumen, CancellationToken cancellationToken)
    {
        ExigirTransaccion();
        contexto.ResumenesPartido.Add(resumen);
        try
        {
            await contexto.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (ErroresDeBase.EsUnicidadDe(ex, ConfiguracionDeIa.IndiceDeResumenPorHuella))
        {
            // Otro trabajador guardó esa huella (R-22): cuenta como hecho. La transacción sigue usable gracias al savepoint
            // automático (ADR-02 hecho 10), y la fila fallida sale del rastreador.
            contexto.Entry(resumen).State = EntityState.Detached;
            return false;
        }
    }

    private void ExigirTransaccion()
    {
        if (contexto.Database.CurrentTransaction is null)
            throw new TransaccionRequeridaException();
    }
}
