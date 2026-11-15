using GoalFlow.Application.Estadisticas;
using GoalFlow.Application.Ligas;
using GoalFlow.Application.Tenancy;
using GoalFlow.Domain.Torneos;

namespace GoalFlow.Api.Hosting;

/// <summary>
/// <c>GET /{slug}/torneos/{codigo}/fases/{orden}/posiciones</c>, <c>/goleadores</c> y <c>/valla</c> (Spec 009 R-26). Públicos, sin
/// autenticación. Solo se mapean si <c>Estadisticas:EndpointsPublicosHabilitados</c> es <c>true</c> (R-37): apagados, las rutas no
/// existen y responden <c>404</c> como cualquier ruta desconocida, sin ejecutar ningún caso de uso.
/// </summary>
public static class EstadisticasEndpoints
{
    public const string ClaveDelInterruptor = "Estadisticas:EndpointsPublicosHabilitados";

    /// <summary>El interruptor se lee al arrancar. Ausente o cualquier valor que no sea <c>true</c> es apagado.</summary>
    public static bool Habilitado(IConfiguration configuracion) => configuracion.GetValue<bool>(ClaveDelInterruptor);

    public static IEndpointRouteBuilder MapGoalFlowEstadisticas(this IEndpointRouteBuilder app)
    {
        app.MapGet("/{slug}/torneos/{codigo}/fases/{orden}/posiciones", async (
            string slug, string codigo, string orden, IResolutorDeSlug resolutor, ITenantContext tenantContext,
            ConsultaDePosiciones consulta, CancellationToken cancellationToken) =>
        {
            // Malformada: 400 antes de tocar la base (R-26).
            if (!FormatoDeCodigos.EsValido(codigo) || !int.TryParse(orden, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var fase) || fase < 1)
                return Results.BadRequest();
            if (await Resolver(slug, resolutor, tenantContext, cancellationToken) is false) return Results.NotFound();

            return Responder(await consulta.EjecutarAsync(codigo, fase, cancellationToken));
        });

        app.MapGet("/{slug}/torneos/{codigo}/goleadores", async (
            string slug, string codigo, IResolutorDeSlug resolutor, ITenantContext tenantContext,
            ConsultaDeGoleadores consulta, CancellationToken cancellationToken) =>
        {
            if (!FormatoDeCodigos.EsValido(codigo)) return Results.BadRequest();
            if (await Resolver(slug, resolutor, tenantContext, cancellationToken) is false) return Results.NotFound();

            return Responder(await consulta.EjecutarAsync(codigo, cancellationToken));
        });

        app.MapGet("/{slug}/torneos/{codigo}/valla", async (
            string slug, string codigo, IResolutorDeSlug resolutor, ITenantContext tenantContext,
            ConsultaDeValla consulta, CancellationToken cancellationToken) =>
        {
            if (!FormatoDeCodigos.EsValido(codigo)) return Results.BadRequest();
            if (await Resolver(slug, resolutor, tenantContext, cancellationToken) is false) return Results.NotFound();

            return Responder(await consulta.EjecutarAsync(codigo, cancellationToken));
        });

        return app;
    }

    /// <returns><c>false</c> si la liga no existe; si existe, fija el tenant del contexto.</returns>
    private static async Task<bool> Resolver(
        string slug, IResolutorDeSlug resolutor, ITenantContext tenantContext, CancellationToken cancellationToken)
    {
        var liga = await resolutor.ResolverAsync(slug, cancellationToken);
        if (liga is null) return false;

        tenantContext.Establecer(liga.Id);
        return true;
    }

    private static IResult Responder<T>(ResultadoDeConsulta<T> resultado) where T : class => resultado.Estado switch
    {
        EstadoDeConsulta.Ok => Results.Ok(resultado.Valor),
        EstadoDeConsulta.ConfiguracionPendiente => Results.Json(
            new { codigo = "configuracion_pendiente", faltantes = resultado.Faltantes }, statusCode: StatusCodes.Status409Conflict),
        _ => Results.NotFound(),
    };
}
