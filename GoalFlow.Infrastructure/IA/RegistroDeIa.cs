using GoalFlow.Application.IA;
using Microsoft.Extensions.DependencyInjection;

namespace GoalFlow.Infrastructure.IA;

public static class RegistroDeIa
{
    /// <summary>
    /// Registra los puertos del módulo IA y sus adaptadores de base de datos. El consumidor, el backfill y el cliente HTTP
    /// se agregan solo con <c>IA:Habilitada</c> en true (Spec 007 R-07).
    /// </summary>
    public static IServiceCollection AddGoalFlowIA(this IServiceCollection services)
    {
        services.AddScoped<IAlmacenDePresupuesto, AlmacenDePresupuesto>();
        return services;
    }
}
