using GoalFlow.Application.IA;
using GoalFlow.Application.Torneos.Contracts;
using GoalFlow.Infrastructure.Mensajeria;
using GoalFlow.Infrastructure.Torneos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace GoalFlow.Infrastructure.IA;

public static class RegistroDeIa
{
    /// <summary>
    /// Registra los puertos del módulo IA y sus adaptadores de base de datos. Con <paramref name="configuracion"/> y
    /// <c>IA:Habilitada</c> en true, registra además la validación de opciones y el consumidor <c>ia.resumen-partido</c>
    /// (Spec 007 R-06, R-07). Apagada, el consumidor no existe: guardar una planilla no escribe ninguna entrega de A-01.
    /// </summary>
    public static IServiceCollection AddGoalFlowIA(this IServiceCollection services, IConfiguration? configuracion = null)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<IAlmacenDePresupuesto, AlmacenDePresupuesto>();
        services.AddScoped<IAlmacenDeLlamadas, AlmacenDeLlamadas>();
        services.AddScoped<IAlmacenDeResumenes, AlmacenDeResumenes>();
        services.AddScoped<ILectorDePartidoParaResumen, LectorDePartidoParaResumen>();

        services.AddOptions<OpcionesDeIA>().BindConfiguration(OpcionesDeIA.Seccion);
        services.TryAddSingleton(proveedor => proveedor.GetRequiredService<IOptions<OpcionesDeIA>>().Value);

        if (configuracion?.GetValue<bool>($"{OpcionesDeIA.Seccion}:Habilitada") == true)
        {
            services.AddOptions<OpcionesDeIA>().ValidateOnStart();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<OpcionesDeIA>, ValidadorDeOpcionesDeIA>());
            services.AddConsumidorDeEvento<PartidoGuardado, ConsumidorDeResumenDePartido>();

            // El único camino al proveedor: un HttpClient tipado y propio, sin Polly ni paquetes nuevos (ADR-12 §2.1). El límite lo
            // pone el consumidor con TimeoutDeLlamada, no el cliente. Apagada la IA, esto no se registra (R-07).
            services.AddHttpClient<IGeneradorDeTexto, AdaptadorAnthropic>(cliente =>
            {
                cliente.BaseAddress = new Uri(AdaptadorAnthropic.UrlBase);
                cliente.Timeout = Timeout.InfiniteTimeSpan;
            });
        }

        return services;
    }
}
