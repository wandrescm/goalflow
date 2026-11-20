using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using GoalFlow.Api.Hosting;
using GoalFlow.Application.Ligas;
using GoalFlow.Domain.Ligas;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoalFlow.Tests.Api;

/// <summary>
/// Spec 011 T-10 (R-24): las tres rutas son de clase <c>publica</c> bajo el limitador global, sin política propia ni exclusión, y una
/// petición rechazada no ejecuta código del endpoint. La aplicación real <b>sin base de datos</b>, como <see cref="LimiteDeFrecuenciaTests"/>.
/// </summary>
public class LecturasPublicasLimiteTests
{
    private const string Ip = "192.0.2.90";

    private static readonly string[] Rutas =
    [
        "/slug-que-no-existe/torneos",
        "/slug-que-no-existe/torneos/T1/fases/1/partidos",
        "/slug-que-no-existe/torneos/T1/fases/1/partidos/E1/E2",
    ];

    private sealed class ResolutorQueCuenta : IResolutorDeSlug
    {
        private int _llamadas;

        public int Llamadas => Volatile.Read(ref _llamadas);

        public Task<Liga?> ResolverAsync(string slug, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _llamadas);
            return Task.FromResult<Liga?>(null);
        }
    }

    private static WebApplicationFactory<Program> Anfitrion(int publicaPorCliente, ResolutorQueCuenta resolutor) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("LimiteDeFrecuencia:Publica:PorCliente", publicaPorCliente.ToString(CultureInfo.InvariantCulture));
            // Inerte: el DbContext solo se construye, nunca se conecta; el doble del resolutor corta antes de cualquier consulta.
            b.UseSetting("ConnectionStrings:GoalFlow", "Host=127.0.0.1;Port=1;Database=prueba;Username=prueba");
            b.ConfigureTestServices(servicios =>
            {
                servicios.RemoveAll<IResolutorDeSlug>();
                servicios.AddSingleton<IResolutorDeSlug>(resolutor);
            });
        });

    private static Task<HttpResponseMessage> Pedir(HttpClient cliente, string ruta)
    {
        var peticion = new HttpRequestMessage(HttpMethod.Get, ruta);
        peticion.Headers.Add("CF-Connecting-IP", Ip);
        return cliente.SendAsync(peticion);
    }

    [Theory] // T-10: la clasificación, con un slug desconocido como en la Spec 010 T-11
    [InlineData("/slug-que-no-existe/torneos")]
    [InlineData("/slug-que-no-existe/torneos/T1/fases/1/partidos")]
    [InlineData("/slug-que-no-existe/torneos/T1/fases/1/partidos/E1/E2")]
    public void T10_las_tres_rutas_son_de_clase_publica(string ruta) =>
        Assert.Equal(ClaseDeRuta.Publica, ParticionDeFrecuencia.ClaseDe(new PathString(ruta)));

    [Theory] // T-10: más allá de su cuota, 429 con el cuerpo y el Retry-After de la Spec 010 y sin llegar al resolutor
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task T10_una_peticion_mas_alla_de_la_cuota_da_429_con_Retry_After_y_no_ejecuta_codigo_del_endpoint(int indice)
    {
        var resolutor = new ResolutorQueCuenta();
        await using var fabrica = Anfitrion(publicaPorCliente: 1, resolutor);
        var cliente = fabrica.CreateClient();
        var ruta = Rutas[indice];

        using var aceptada = await Pedir(cliente, ruta);
        Assert.Equal(HttpStatusCode.NotFound, aceptada.StatusCode); // el slug no existe: el control positivo llegó al endpoint
        Assert.Equal(1, resolutor.Llamadas);

        using var rechazada = await Pedir(cliente, ruta);

        Assert.Equal(HttpStatusCode.TooManyRequests, rechazada.StatusCode);
        Assert.True(rechazada.Headers.Contains("Retry-After"));
        Assert.Equal("limite_de_frecuencia", (string?)JsonNode.Parse(await rechazada.Content.ReadAsStringAsync())!["error"]);
        Assert.Equal(1, resolutor.Llamadas); // ninguna llamada nueva: la petición rechazada no corrió código del endpoint
    }
}
