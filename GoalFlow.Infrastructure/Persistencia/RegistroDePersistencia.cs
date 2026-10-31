using GoalFlow.Application.Ligas;
using GoalFlow.Application.Persistencia;
using GoalFlow.Application.Tenancy;
using GoalFlow.Application.Torneos;
using GoalFlow.Infrastructure.Ligas;
using GoalFlow.Infrastructure.Torneos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GoalFlow.Infrastructure.Persistencia;

public static class RegistroDePersistencia
{
    private const string NombreDeCadena = "GoalFlow";

    /// <summary>
    /// Todo es scoped y sin pooling de DbContext (ADR-14 §2.3). La cadena se lee al resolver el
    /// DbContext, no al arrancar: /health debe responder sin base de datos (ADR-05 R-04).
    /// </summary>
    public static IServiceCollection AddGoalFlowPersistencia(this IServiceCollection services)
    {
        services.AddScoped<ITenantContext, ContextoDeTenant>();
        services.AddScoped<IIdentidadAutenticada, IdentidadAutenticada>();
        services.AddScoped<InterceptorDeTransacciones>();
        services.AddScoped<InterceptorDeGuardado>();
        services.AddScoped<IUnidadDeTrabajo, UnidadDeTrabajo>();
        services.AddScoped<IResolutorDeSlug, ResolutorDeSlug>();
        services.AddScoped<ILectorDeEquipos, LectorDeEquipos>();
        services.AddScoped<ConsultaDeEquipos>();

        services.AddDbContext<GoalFlowDbContext>((proveedor, opciones) =>
        {
            var cadena = proveedor.GetRequiredService<IConfiguration>().GetConnectionString(NombreDeCadena)
                ?? throw new InvalidOperationException($"Falta la cadena de conexión {NombreDeCadena}.");

            ConfigurarNpgsql(opciones, cadena);
            opciones.AddInterceptors(
                proveedor.GetRequiredService<InterceptorDeTransacciones>(),
                proveedor.GetRequiredService<InterceptorDeGuardado>());
        });

        return services;
    }

    public static void ConfigurarNpgsql(DbContextOptionsBuilder opciones, string? cadena) =>
        opciones.UseNpgsql(cadena, npgsql =>
            npgsql.MigrationsHistoryTable(HistoryRepository.DefaultTableName, GoalFlowDbContext.Esquema));
}
