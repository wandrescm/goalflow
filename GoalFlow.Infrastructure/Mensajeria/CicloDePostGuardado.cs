using GoalFlow.Application.Mensajeria;
using GoalFlow.Application.Persistencia;
using GoalFlow.Application.Tenancy;
using GoalFlow.Domain.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GoalFlow.Infrastructure.Mensajeria;

/// <summary>
/// El único ciclo en segundo plano del pipeline post-guardado (ADR-02 §2.5). La primera pasada es la recuperación
/// tras un arranque; después espera una pista del canal o el temporizador. Recorre los tenants uno tras otro, con
/// un scope por tenant y otro por entrega, y nunca tiene más de una conexión abierta. Nada de lo scoped se
/// captura aquí: todo se resuelve dentro de los scopes que crea (R-19).
/// </summary>
public sealed class CicloDePostGuardado(
    IServiceScopeFactory scopes,
    CanalDeAvisos canal,
    IOptions<OpcionesDeMensajeria> opciones,
    IEnumerable<RegistroDeConsumidor> registros,
    ILogger<CicloDePostGuardado> logger) : BackgroundService
{
    private readonly OpcionesDeMensajeria _o = opciones.Value;
    private readonly IReadOnlyList<RegistroDeConsumidor> _registros = [.. registros];
    private long _pasadasCompletadas;

    /// <summary>Pasadas que terminaron sin excepción. Para pruebas y diagnóstico.</summary>
    public long PasadasCompletadas => Interlocked.Read(ref _pasadasCompletadas);

    protected override async Task ExecuteAsync(CancellationToken detencion)
    {
        // El primer await va antes de cualquier trabajo síncrono: el arranque del host no espera a la primera pasada.
        await Task.Yield();

        if (!_o.CicloHabilitado)
        {
            logger.LogInformation("Ciclo post-guardado deshabilitado por configuración (Mensajeria:CicloHabilitado).");
            return;
        }

        var primera = true;
        while (!detencion.IsCancellationRequested)
        {
            try
            {
                await EjecutarPasadaAsync(primera, detencion);
                Interlocked.Increment(ref _pasadasCompletadas);
                primera = false;
                await EsperarAvisoOTemporizadorAsync(detencion);
            }
            catch (OperationCanceledException) when (detencion.IsCancellationRequested)
            {
                break; // Apagado: no se escribe nada más (R-20).
            }
            catch (Exception ex)
            {
                // R-44: ninguna excepción tumba el host. Solo el tipo y la traza, nunca el mensaje (R-14).
                logger.LogWarning(
                    "La pasada del ciclo post-guardado falló y se reintentará en la siguiente. Tipo {TipoDeExcepcion}, internas {TiposInternos}, traza {Traza}",
                    ex.GetType().FullName, TiposInternos(ex), ex.StackTrace);
                try
                {
                    await EsperarAvisoOTemporizadorAsync(detencion);
                }
                catch (OperationCanceledException) when (detencion.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    private async Task EjecutarPasadaAsync(bool primera, CancellationToken detencion)
    {
        IReadOnlyList<TenantId> tenants;
        await using (var scope = scopes.CreateAsyncScope())
        {
            tenants = await scope.ServiceProvider.GetRequiredService<IFuenteDeTenants>().ListarAsync(detencion);
        }

        var procesadas = 0;
        if (_registros.Count > 0)
        {
            foreach (var tenant in tenants)
            {
                detencion.ThrowIfCancellationRequested();
                procesadas += await ProcesarTenantAsync(tenant, detencion);
            }
        }

        // R-43: la primera pasada deja constancia en producción; las demás, solo si hubo trabajo.
        if (primera)
            logger.LogInformation(
                "Primera pasada del ciclo post-guardado completada sobre {Ligas} ligas, {Consumidores} consumidores registrados y {Entregas} entregas procesadas.",
                tenants.Count, _registros.Count, procesadas);
        else if (procesadas > 0)
            logger.LogInformation("Pasada del ciclo post-guardado: {Entregas} entregas procesadas en {Ligas} ligas.", procesadas, tenants.Count);
    }

    private async Task<int> ProcesarTenantAsync(TenantId tenant, CancellationToken detencion)
    {
        var reclamadas = await ReclamarLoteAsync(tenant, detencion);

        // La transacción y el scope de la reclamación ya se cerraron: ninguna conexión sigue abierta mientras
        // trabaja un consumidor (R-22, R-29).
        foreach (var entrega in reclamadas)
        {
            detencion.ThrowIfCancellationRequested();
            await ProcesarEntregaAsync(entrega, detencion);
        }

        return reclamadas.Count;
    }

    private async Task<IReadOnlyList<EntregaReclamada>> ReclamarLoteAsync(TenantId tenant, CancellationToken detencion)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().Establecer(tenant);
        var operaciones = scope.ServiceProvider.GetRequiredService<OperacionesDeEntrega>();

        await using var transaccion = await scope.ServiceProvider.GetRequiredService<IUnidadDeTrabajo>().IniciarAsync(detencion);
        var reclamadas = new List<EntregaReclamada>();
        foreach (var candidata in await operaciones.ListarCandidatasAsync(detencion))
        {
            if (candidata.Intentos >= _o.MaximoDeIntentos)
            {
                await operaciones.AgotarAsync(candidata, detencion); // Nunca llega a un consumidor (R-28).
                continue;
            }

            if (await operaciones.ReclamarAsync(candidata, detencion) is { } reclamada)
                reclamadas.Add(reclamada);
        }

        await transaccion.ConfirmarAsync(detencion);
        return reclamadas;
    }

    private async Task ProcesarEntregaAsync(EntregaReclamada entrega, CancellationToken detencion)
    {
        var registro = _registros.First(r => r.Nombre == entrega.Consumidor);
        Exception? error = null;

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ITenantContext>().Establecer(entrega.Tenant);

            using var limite = CancellationTokenSource.CreateLinkedTokenSource(detencion);
            limite.CancelAfter(_o.TimeoutDelConsumidor);
            try
            {
                await registro.Procesar(scope.ServiceProvider, entrega, limite.Token);
            }
            catch (OperationCanceledException) when (!detencion.IsCancellationRequested && limite.IsCancellationRequested)
            {
                error = new TimeoutException(); // R-30: el timeout es un fallo reintentable.
            }
        }
        catch (OperationCanceledException) when (detencion.IsCancellationRequested)
        {
            throw; // R-20: el apagado no es un fallo del consumidor; no se escribe nada y el lease vence solo.
        }
        catch (Exception ex)
        {
            error = ex;
        }

        await RegistrarResultadoAsync(entrega, error, detencion);
    }

    private async Task RegistrarResultadoAsync(EntregaReclamada entrega, Exception? error, CancellationToken detencion)
    {
        // Transacción corta y propia, en un scope nuevo, igual que la reclamación (R-31 a R-33).
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().Establecer(entrega.Tenant);
        var operaciones = scope.ServiceProvider.GetRequiredService<OperacionesDeEntrega>();
        await using var transaccion = await scope.ServiceProvider.GetRequiredService<IUnidadDeTrabajo>().IniciarAsync(detencion);

        if (error is null)
        {
            await operaciones.CompletarAsync(entrega, detencion);
        }
        else
        {
            RegistrarFallo(entrega, error);
            if (error is FalloPermanenteException permanente)
                await operaciones.FallarAsync(entrega, $"{nameof(FalloPermanenteException)}:{permanente.CodigoDeRazon}", detencion);
            else
                await operaciones.ReintentarAsync(entrega, error.GetType().Name, detencion);
        }

        await transaccion.ConfirmarAsync(detencion);
    }

    /// <summary>R-14: ids, tipo, tipos de las internas y traza. Nunca el mensaje, y nunca el objeto excepción.</summary>
    private void RegistrarFallo(EntregaReclamada entrega, Exception error) =>
        logger.LogWarning(
            "Falló la entrega {EntregaId}. Tenant {Tenant}, evento {Evento}, consumidor {Consumidor}, intento {Intento}, tipo {TipoDeExcepcion}, internas {TiposInternos}, traza {Traza}",
            entrega.Id, entrega.Tenant.Value, entrega.TipoDeEvento, entrega.Consumidor, entrega.Intentos,
            error.GetType().FullName, TiposInternos(error), error.StackTrace);

    private static string TiposInternos(Exception error)
    {
        var tipos = new List<string>();
        for (var interna = error.InnerException; interna is not null; interna = interna.InnerException)
            tipos.Add(interna.GetType().FullName ?? interna.GetType().Name);
        return string.Join(" > ", tipos);
    }

    /// <summary>Espera una pista o el temporizador, lo que ocurra primero (R-16), y vacía las pistas acumuladas.</summary>
    private async Task EsperarAvisoOTemporizadorAsync(CancellationToken detencion)
    {
        using var cancelar = CancellationTokenSource.CreateLinkedTokenSource(detencion);
        var pista = canal.Lector.WaitToReadAsync(cancelar.Token).AsTask();
        var temporizador = Task.Delay(_o.PeriodoDelTemporizador, cancelar.Token);

        await Task.WhenAny(pista, temporizador);
        await cancelar.CancelAsync();

        // Un solo lector a la vez: se espera a que la lectura cancelada termine antes de la próxima.
        try
        {
            await Task.WhenAll(pista, temporizador);
        }
        catch (OperationCanceledException)
        {
            detencion.ThrowIfCancellationRequested();
        }

        while (canal.Lector.TryRead(out _))
        {
            // Una pasada recorre todos los tenants: basta con saber que hubo al menos una.
        }
    }
}
