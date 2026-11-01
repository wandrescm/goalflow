using System.Net;
using System.Net.Http.Json;
using GoalFlow.Application.Torneos;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using static GoalFlow.Tests.Isolation.BaseDeAislamiento;

namespace GoalFlow.Tests.Isolation;

/// <summary>Spec 002 T-15: API sobre una base real.</summary>
[Collection(ColeccionDeAislamiento.Nombre)]
public class EquiposEndpointTests(BaseDeAislamiento db)
{
    private WebApplicationFactory<Program> Fabrica() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.UseSetting("ConnectionStrings:GoalFlow", db.CadenaApp));

    [Fact]
    public async Task T15_slug_existente_devuelve_solo_los_equipos_de_esa_liga()
    {
        await using var fabrica = Fabrica();
        var cliente = fabrica.CreateClient();

        var equipos = await cliente.GetFromJsonAsync<List<EquipoResumen>>("/liga-a/equipos");

        Assert.NotNull(equipos);
        Assert.Contains(equipos, e => e.Nombre == EquipoDeA);
        Assert.DoesNotContain(equipos, e => e.Nombre == EquipoDeB);
    }

    [Fact]
    public async Task T15_slug_inexistente_devuelve_404()
    {
        await using var fabrica = Fabrica();
        var cliente = fabrica.CreateClient();

        var respuesta = await cliente.GetAsync("/no-existe/equipos");

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
    }
}
