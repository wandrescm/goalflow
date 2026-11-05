using GoalFlow.Application.Mensajeria;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GoalFlow.Infrastructure.Mensajeria;

public static class RegistroDeMensajeria
{
    /// <summary>Opciones validadas al arrancar, publicador, avisos y operaciones de entrega. Idempotente: Persistencia también lo llama, porque el DbContext lleva el interceptor de avisos.</summary>
    internal static IServiceCollection AddNucleoDeMensajeria(this IServiceCollection services)
    {
        services.AddLogging();
        services.AddOptions<OpcionesDeMensajeria>()
            .BindConfiguration(OpcionesDeMensajeria.Seccion)
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<OpcionesDeMensajeria>, ValidadorDeOpcionesDeMensajeria>());
        services.TryAddSingleton<CanalDeAvisos>();
        services.TryAddScoped<AvisosPendientes>();
        services.TryAddScoped<InterceptorDeAvisos>();
        services.TryAddScoped<IPublicadorDeEventos, PublicadorDeEventos>();
        services.TryAddScoped<OperacionesDeEntrega>();

        return services;
    }

    /// <summary>Todo lo anterior más el ciclo en segundo plano. Es la línea que agrega <c>Program.cs</c>.</summary>
    public static IServiceCollection AddGoalFlowMensajeria(this IServiceCollection services)
    {
        services.AddNucleoDeMensajeria();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, CicloDePostGuardado>());
        return services;
    }

    /// <summary>
    /// Registra un consumidor scoped. <paramref name="nombre"/> reemplaza la constante del consumidor y existe
    /// para pruebas (nombres únicos por prueba); en producción se omite.
    /// </summary>
    public static IServiceCollection AddConsumidorDeEvento<TEvento, TConsumidor>(
        this IServiceCollection services, string? nombre = null)
        where TEvento : IEventoDeIntegracion
        where TConsumidor : class, IConsumidorDeEvento<TEvento>
    {
        services.TryAddScoped<TConsumidor>();
        services.AddSingleton(RegistroDeConsumidor.Crear<TEvento, TConsumidor>(nombre));
        return services;
    }
}
