using GoalFlow.Application.IA;
using GoalFlow.Application.Mensajeria;
using GoalFlow.Application.Torneos.Contracts;
using GoalFlow.Infrastructure.Persistencia;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GoalFlow.Infrastructure.IA;

/// <summary>El adaptador del puerto: la publicación dirigida y el guardado, en la transacción que abrió el backfill (Spec 007 R-31, R-32).</summary>
public sealed class PublicacionDeBackfill(IPublicadorDeEventos publicador, GoalFlowDbContext contexto) : IPublicacionDeBackfill
{
    public async Task PublicarAsync(PartidoGuardado evento, CancellationToken cancellationToken)
    {
        publicador.PublicarPara(evento, ConsumidorDeResumenDePartido.NombreDelConsumidor);

        // El duplicado sale de este SaveChanges como EntregaDuplicadaException (Spec 004 R-09).
        await contexto.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// Corre el backfill <b>una vez por arranque del proceso</b> y solo se registra con <c>IA:Habilitada</c> e
/// <c>IA:Backfill:EjecutarAlArrancar</c> en true (Spec 007 R-32). Nunca tumba el host: cualquier excepción se registra como tipo.
/// No hay endpoint: el propietario enciende la bandera para un despliegue y la quita.
/// </summary>
public sealed class RellenoAlArrancar(BackfillDeResumenes backfill, ILogger<RellenoAlArrancar> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken detencion)
    {
        // El primer await va antes de cualquier trabajo: el arranque del host no espera al backfill.
        await Task.Yield();
        try
        {
            await backfill.EjecutarAsync(detencion);
        }
        catch (OperationCanceledException) when (detencion.IsCancellationRequested)
        {
            // Apagado: no hay nada más que hacer.
        }
        catch (Exception ex)
        {
            logger.LogWarning("El backfill de A-01 falló y no se repite en este arranque. Tipo {TipoDeExcepcion}.", ex.GetType().FullName);
        }
    }
}
