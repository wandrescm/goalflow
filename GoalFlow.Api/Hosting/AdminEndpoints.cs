using GoalFlow.Application.Ligas;
using GoalFlow.Application.Tenancy;
using Microsoft.AspNetCore.Authentication;

namespace GoalFlow.Api.Hosting;

public static class AdminEndpoints
{
    public sealed record SesionDeAdmin(string Slug, string Rol);

    public static IEndpointRouteBuilder MapGoalFlowAdmin(this IEndpointRouteBuilder app)
    {
        // No RequireAuthorization: it would answer 401 before the slug is resolved (ADR-01 §2.2).
        var admin = app.MapGroup("/{slug}/admin").AddEndpointFilter<FiltroDeAdmin>();

        admin.MapGet("/sesion", (HttpContext context) => Results.Ok(context.Features.Get<SesionDeAdmin>()));

        admin.MapImportacionDeTorneo();

        return app;
    }

    /// <summary>Slug (404), token (401), membership (403), in that order.</summary>
    private sealed class FiltroDeAdmin(
        IResolutorDeSlug resolutor,
        ITenantContext tenantContext,
        IIdentidadAutenticada identidad,
        ConsultaDeMembresia membresia) : IEndpointFilter
    {
        public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            var http = context.HttpContext;
            var liga = await resolutor.ResolverAsync((string)http.GetRouteValue("slug")!, http.RequestAborted);
            if (liga is null) return Results.NotFound();

            var autenticacion = await http.AuthenticateAsync(AutenticacionExtensions.Esquema);
            if (!autenticacion.Succeeded || AutenticacionExtensions.UsuarioDe(autenticacion.Principal) is not { } usuario)
            {
                // RFC 9110 requires the header on every 401; no error details (Spec 003 R-08).
                http.Response.Headers.WWWAuthenticate = "Bearer";
                return Results.Unauthorized();
            }

            tenantContext.Establecer(liga.Id);
            identidad.Establecer(usuario);

            var rol = await membresia.EjecutarAsync(http.RequestAborted);
            if (rol is null) return Results.StatusCode(StatusCodes.Status403Forbidden);

            http.Features.Set(new SesionDeAdmin(liga.Slug, rol));
            return await next(context);
        }
    }
}
