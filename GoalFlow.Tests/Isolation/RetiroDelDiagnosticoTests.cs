using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using static GoalFlow.Tests.Isolation.BaseDeAislamiento;

namespace GoalFlow.Tests.Isolation;

/// <summary>Spec 010 R-20 / T-19: el diagnóstico de red ya no existe, ni siquiera con su antiguo interruptor en true.</summary>
[Collection(ColeccionDeAislamiento.Nombre)]
public class RetiroDelDiagnosticoTests(BaseDeAislamiento db)
{
    [Fact]
    public async Task T19_con_el_interruptor_antiguo_en_true_la_ruta_del_diagnostico_devuelve_404_a_un_Admin_valido()
    {
        var llave = new LlaveDePrueba();
        await using var fabrica = AnfitrionDeAdmin.Crear(db, new FuenteDeJwksDePrueba(llave)).WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration((_, configuracion) =>
                configuracion.AddInMemoryCollection(new Dictionary<string, string?> { ["Diagnostico:RedHabilitado"] = "true" })));

        // El cliente (y con él el host) se construye antes de firmar el token: IdentityModel fija interruptores de proceso al registrar.
        var cliente = fabrica.CreateClient();
        var peticion = new HttpRequestMessage(HttpMethod.Get, "/liga-a/admin/diagnostico/red");
        peticion.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TokensDePrueba.Firmar(llave, UsuarioU1));

        var respuesta = await cliente.SendAsync(peticion);

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
    }
}
