using System.Globalization;
using GoalFlow.Application.Ligas;
using GoalFlow.Application.Tenancy;
using GoalFlow.Application.Torneos;
using GoalFlow.Domain.Torneos;

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

        app.MapGet("/{slug}/torneos/{codigo}/fases/{orden}/partidos", async (
            string slug, string codigo, string orden, IResolutorDeSlug resolutor, ITenantContext tenantContext,
            ConsultaDePartidosDeFase consulta, CancellationToken cancellationToken) =>
        {
            // Malformada: 400 antes de resolver el slug y de tocar la base (R-03, R-31). Los parámetros son string, sin restricciones
            // de ruta: una restricción convertiría el 400 en 404.
            if (!FormatoDeCodigos.EsValido(codigo) || !EsOrdenValido(orden, out var fase)) return Results.BadRequest();
            if (await Resolver(slug, resolutor, tenantContext, cancellationToken) is false) return Results.NotFound();

            var resultado = await consulta.EjecutarAsync(codigo, fase, cancellationToken);
            return resultado.Encontrado ? Results.Ok(resultado.Partidos) : Results.NotFound();
        });

        return app;
    }

    /// <summary>El mismo parser que los endpoints de estadísticas (Spec 011 C-25): solo dígitos, cultura invariante, valor ≥ 1.</summary>
    private static bool EsOrdenValido(string orden, out int fase) =>
        int.TryParse(orden, NumberStyles.None, CultureInfo.InvariantCulture, out fase) && fase >= 1;

    /// <returns><c>false</c> si la liga no existe; si existe, fija el tenant del contexto.</returns>
    private static async Task<bool> Resolver(
        string slug, IResolutorDeSlug resolutor, ITenantContext tenantContext, CancellationToken cancellationToken)
    {
        var liga = await resolutor.ResolverAsync(slug, cancellationToken);
        if (liga is null) return false;

        tenantContext.Establecer(liga.Id);
        return true;
    }
}
