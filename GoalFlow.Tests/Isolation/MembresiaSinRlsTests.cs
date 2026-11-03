using GoalFlow.Application.Ligas;
using GoalFlow.Application.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using static GoalFlow.Tests.Isolation.BaseDeAislamiento;

namespace GoalFlow.Tests.Isolation;

/// <summary>Spec 003 T-14: como superusuario RLS no aplica, así que solo queda el filtro del adaptador.</summary>
[Collection(ColeccionDeAislamiento.Nombre)]
public class MembresiaSinRlsTests(BaseDeAislamiento db)
{
    private async Task<string?> RolEnLigaA(Guid usuario)
    {
        await using var servicios = db.CrearServiciosComoSuperusuario();
        await using var scope = servicios.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().Establecer(LigaA);
        scope.ServiceProvider.GetRequiredService<IIdentidadAutenticada>().Establecer(usuario);

        return await scope.ServiceProvider.GetRequiredService<ConsultaDeMembresia>().EjecutarAsync(CancellationToken.None);
    }

    [Fact]
    public async Task T14_un_usuario_sin_membresia_en_la_liga_no_recibe_el_rol_de_otro_Admin()
    {
        Assert.Null(await RolEnLigaA(UsuarioU2));
    }

    [Fact]
    public async Task T14_control_el_Admin_de_la_liga_si_recibe_su_rol()
    {
        Assert.Equal("admin", await RolEnLigaA(UsuarioU1));
    }
}
