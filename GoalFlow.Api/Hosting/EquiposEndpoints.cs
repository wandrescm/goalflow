using GoalFlow.Application.Ligas;
using GoalFlow.Application.Tenancy;
using GoalFlow.Application.Torneos;

namespace GoalFlow.Api.Hosting;

public static class EquiposEndpoints
{
    public static IEndpointRouteBuilder MapGoalFlowEquipos(this IEndpointRouteBuilder app)
    {
        app.MapGet("/{slug}/equipos", async (
            string slug,
            IResolutorDeSlug resolutor,
            ITenantContext tenantContext,
            ConsultaDeEquipos consulta,
            CancellationToken cancellationToken) =>
        {
            var liga = await resolutor.ResolverAsync(slug, cancellationToken);
            if (liga is null) return Results.NotFound();

            tenantContext.Establecer(liga.Id);
            return Results.Ok(await consulta.EjecutarAsync(cancellationToken));
        });

        return app;
    }
}
