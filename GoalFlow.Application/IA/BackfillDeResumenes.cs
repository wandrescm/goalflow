using GoalFlow.Application.Mensajeria;
using GoalFlow.Application.Persistencia;
using GoalFlow.Application.Tenancy;
using GoalFlow.Application.Torneos.Contracts;
using GoalFlow.Domain.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GoalFlow.Application.IA;

/// <summary>
/// Publica <see cref="PartidoGuardado"/> <b>solo para A-01</b> y guarda, dentro de la transacción abierta de quien llama (Spec 007 R-32).
/// La publicación dirigida no hace fan-out: nunca toca las filas de otros consumidores (ADR-12 C-12).
/// </summary>
public interface IPublicacionDeBackfill
{
    /// <exception cref="EntregaDuplicadaException">Ya existe la fila de A-01 para esa versión del partido.</exception>
    Task PublicarAsync(PartidoGuardado evento, CancellationToken cancellationToken);
}

/// <summary>Los conteos de una corrida del backfill (R-32).</summary>
public sealed record ResultadoDeBackfill(int Tenants, int Creadas, int YaPresentes, int Fallidas);

/// <summary>
/// El backfill de A-01 (Spec 007 R-32, ADR-12 §2.8): cubre los partidos guardados mientras A-01 estaba apagada. Por cada liga lista
/// los partidos con resultado (R-34) y, por cada partido, en su <b>propio scope y su propia transacción</b>, publica
/// <c>PartidoGuardado(tenant, partido, versión vigente)</c> solo para <c>ia.resumen-partido</c> y confirma. Captura exactamente
/// <see cref="EntregaDuplicadaException"/>, descarta ese scope y cuenta el partido como ya presente: correrlo de nuevo, o en dos
/// instancias a la vez, es seguro. Cualquier otra excepción se registra solo como tipo y la corrida sigue; solo la cancelación la termina.
/// </summary>
public sealed class BackfillDeResumenes(IServiceScopeFactory scopes, ILogger<BackfillDeResumenes> logger)
{
    public async Task<ResultadoDeBackfill> EjecutarAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<TenantId> tenants;
        await using (var scope = scopes.CreateAsyncScope())
            tenants = await scope.ServiceProvider.GetRequiredService<IFuenteDeTenants>().ListarAsync(cancellationToken);

        int creadas = 0, presentes = 0, fallidas = 0;
        foreach (var tenant in tenants)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IReadOnlyList<PartidoConResultado> partidos;
            try
            {
                partidos = await ListarAsync(tenant, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                fallidas++;
                logger.LogWarning("El backfill de A-01 no pudo listar los partidos de una liga. Tenant {Tenant}, tipo {TipoDeExcepcion}.", tenant.Value, ex.GetType().FullName);
                continue;
            }

            foreach (var partido in partidos)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await PublicarAsync(tenant, partido, cancellationToken);
                    creadas++;
                }
                catch (EntregaDuplicadaException)
                {
                    presentes++; // el scope ya se descartó: la fila de A-01 existía
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    fallidas++;
                    logger.LogWarning(
                        "El backfill de A-01 falló con un partido y sigue con el siguiente. Tenant {Tenant}, partido {Partido}, tipo {TipoDeExcepcion}.",
                        tenant.Value, partido.PartidoId, ex.GetType().FullName);
                }
            }
        }

        logger.LogInformation(
            "Backfill de A-01 terminado: ligas {Ligas}, creadas {Creadas}, ya presentes {YaPresentes}, fallidas {Fallidas}.",
            tenants.Count, creadas, presentes, fallidas);
        return new ResultadoDeBackfill(tenants.Count, creadas, presentes, fallidas);
    }

    private async Task<IReadOnlyList<PartidoConResultado>> ListarAsync(TenantId tenant, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().Establecer(tenant);
        await using var transaccion = await scope.ServiceProvider.GetRequiredService<IUnidadDeTrabajo>().IniciarAsync(cancellationToken);
        var lista = await scope.ServiceProvider.GetRequiredService<IListadoDePartidosConResultado>().ListarAsync(cancellationToken);
        await transaccion.ConfirmarAsync(cancellationToken);
        return lista;
    }

    private async Task PublicarAsync(TenantId tenant, PartidoConResultado partido, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().Establecer(tenant);
        await using var transaccion = await scope.ServiceProvider.GetRequiredService<IUnidadDeTrabajo>().IniciarAsync(cancellationToken);
        await scope.ServiceProvider.GetRequiredService<IPublicacionDeBackfill>()
            .PublicarAsync(new PartidoGuardado(tenant, partido.PartidoId, partido.Version), cancellationToken);
        await transaccion.ConfirmarAsync(cancellationToken);
    }
}
