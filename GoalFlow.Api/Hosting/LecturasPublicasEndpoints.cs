using GoalFlow.Application.Ligas;
using GoalFlow.Application.Tenancy;
using GoalFlow.Application.Torneos;

namespace GoalFlow.Api.Hosting;

/// <summary>
/// Las lecturas públicas de la Spec 011: el catálogo de la liga y los partidos de una fase (Torneos) y el detalle de un partido (IA).
/// Sin autenticación y sin interruptor (R-23, R-27); toda ruta es de clase <c>publica</c> bajo el limitador global (R-24). Los
/// endpoints no tienen reglas de negocio (R-31): validan el borde, resuelven el slug, fijan el tenant y llaman al caso de uso.
/// </summary>
public static class LecturasPublicasEndpoints
{
    public static IEndpointRouteBuilder MapGoalFlowLecturasPublicas(this IEndpointRouteBuilder app)
    {
        app.MapGet("/{slug}/torneos", async (
            string slug, IResolutorDeSlug resolutor, ITenantContext tenantContext,
            ConsultaDeTorneosDeLaLiga consulta, CancellationToken cancellationToken) =>
        {
            var liga = await resolutor.ResolverAsync(slug, cancellationToken);
            if (liga is null) return Results.NotFound();

            tenantContext.Establecer(liga.Id);
            var torneos = await consulta.EjecutarAsync(cancellationToken);
            return Results.Ok(new { liga = new { slug = liga.Slug, nombre = liga.Nombre }, torneos });
        });

        return app;
    }
}
