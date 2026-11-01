using GoalFlow.Application.Persistencia;
using GoalFlow.Application.Tenancy;
using GoalFlow.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using static GoalFlow.Tests.Isolation.BaseDeAislamiento;

namespace GoalFlow.Tests.Isolation;

/// <summary>
/// Spec 002 T-23. IMemoryCache es un singleton del proveedor interno de EF Core: dos instancias
/// iguales significan un mismo proveedor. La caché de proveedores de EF es global al proceso,
/// así que el canario crea uno solo y sin ciclos.
/// </summary>
[Collection(ColeccionDeAislamiento.Nombre)]
public class ProveedoresInternosTests(BaseDeAislamiento db)
{
    private const int ScopesSobreElUmbralDeEfCore = 30;

    private static IMemoryCache CacheInterna(ServiceProvider servicios)
    {
        using var scope = servicios.CreateScope();
        return scope.ServiceProvider.GetRequiredService<GoalFlowDbContext>().GetService<IMemoryCache>();
    }

    [Fact]
    public async Task T23_los_scopes_de_un_mismo_proveedor_comparten_el_proveedor_interno()
    {
        await using var servicios = db.CrearServicios();

        Assert.Same(CacheInterna(servicios), CacheInterna(servicios));

        for (var i = 0; i < ScopesSobreElUmbralDeEfCore; i++)
        {
            await using var scope = servicios.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ITenantContext>().Establecer(LigaA);
            var contexto = scope.ServiceProvider.GetRequiredService<GoalFlowDbContext>();

            await using var transaccion = await scope.ServiceProvider
                .GetRequiredService<IUnidadDeTrabajo>().IniciarAsync(CancellationToken.None);
            Assert.True(await contexto.Equipos.AnyAsync());
        }
    }

    [Fact]
    public async Task T23_canario_una_opcion_de_la_clave_produce_otro_proveedor_interno()
    {
        await using var servicios = db.CrearServicios();
        await using var mismaConfiguracion = db.CrearServicios();
        await using var conOtraClave = db.CrearServicios(s =>
            s.ConfigureDbContext<GoalFlowDbContext>(o => o.EnableSensitiveDataLogging()));

        // Control: la diferencia viene de la opción, no de ser otro proveedor raíz.
        Assert.Same(CacheInterna(servicios), CacheInterna(mismaConfiguracion));
        Assert.NotSame(CacheInterna(servicios), CacheInterna(conOtraClave));
    }

    [Fact]
    public async Task T23_el_registro_real_lanza_con_ManyServiceProvidersCreatedWarning()
    {
        await using var servicios = db.CrearServicios();
        await using var scope = servicios.CreateAsyncScope();
        var contexto = scope.ServiceProvider.GetRequiredService<GoalFlowDbContext>();

        var opciones = contexto.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>();

        Assert.Equal(
            WarningBehavior.Throw,
            opciones?.WarningsConfiguration.GetBehavior(CoreEventId.ManyServiceProvidersCreatedWarning));
    }
}
