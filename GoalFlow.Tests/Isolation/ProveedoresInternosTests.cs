using GoalFlow.Application.Persistencia;
using GoalFlow.Application.Tenancy;
using GoalFlow.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static GoalFlow.Tests.Isolation.BaseDeAislamiento;

namespace GoalFlow.Tests.Isolation;

/// <summary>Spec 002 T-23.</summary>
[Collection(ColeccionDeAislamiento.Nombre)]
public class ProveedoresInternosTests(BaseDeAislamiento db)
{
    private const int ScopesSobreElUmbralDeEfCore = 30;

    [Fact]
    public async Task T23_treinta_scopes_no_crean_un_proveedor_interno_por_scope()
    {
        await using var servicios = db.CrearServicios();

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
}
