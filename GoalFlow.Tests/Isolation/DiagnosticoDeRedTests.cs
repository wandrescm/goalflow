using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using static GoalFlow.Tests.Isolation.BaseDeAislamiento;

namespace GoalFlow.Tests.Isolation;

/// <summary>
/// Spec 010 Parte A (T-01 a T-08): diagnóstico de red temporal. Corren la aplicación real en <c>Production</c>, conectada
/// como goalflow_app bajo RLS, con un <see cref="IStartupFilter"/> que simula al proxy de Render (<c>10.0.0.1</c>).
/// </summary>
[Collection(ColeccionDeAislamiento.Nombre)]
public class DiagnosticoDeRedTests(BaseDeAislamiento db)
{
    private const string Ruta = "/liga-a/admin/diagnostico/red";
    private const string ClaveDelInterruptor = "Diagnostico:RedHabilitado";

    private readonly LlaveDePrueba _k1 = new();

    /// <summary>Fija la IP de la conexión antes de todo el pipeline, como el proxy de Render (mismo valor que ForwardedHeadersTests).</summary>
    private sealed class SimulaAlProxyDeRender : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((contexto, siguiente) =>
            {
                contexto.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.1");
                return siguiente(contexto);
            });
            next(app);
        };
    }

    private sealed record Respuesta(HttpStatusCode Estado, IHeaderDictionary Encabezados, string Cuerpo)
    {
        public JsonElement Json => JsonDocument.Parse(Cuerpo).RootElement;
    }

    /// <summary>
    /// La aplicación real en Production; <paramref name="interruptor"/> null deja la clave ausente. Construye el host al crearlo:
    /// <c>AddGoalFlowAutenticacion</c> fija interruptores de proceso de IdentityModel, y firmar un token antes de que el primer
    /// host exista los dejaría con su valor anterior para el resto de la corrida (afectaría a <c>SesionDeAdminTests</c>).
    /// </summary>
    private WebApplicationFactory<Program> Anfitrion(string? interruptor, CapturaDeLogs? logs = null)
    {
        var fabrica = Configurar(interruptor, logs);
        _ = fabrica.Server;
        return fabrica;
    }

    private WebApplicationFactory<Program> Configurar(string? interruptor, CapturaDeLogs? logs) =>
        AnfitrionDeAdmin.Crear(db, new FuenteDeJwksDePrueba(_k1)).WithWebHostBuilder(b =>
        {
            b.UseEnvironment(Environments.Production);
            if (interruptor is not null)
                b.ConfigureAppConfiguration((_, configuracion) =>
                    configuracion.AddInMemoryCollection(new Dictionary<string, string?> { [ClaveDelInterruptor] = interruptor }));
            b.ConfigureTestServices(servicios => servicios.AddTransient<IStartupFilter, SimulaAlProxyDeRender>());

            if (logs is not null)
            {
                // Categorías GoalFlow en Trace; las demás quedan en los niveles del appsettings.json versionado.
                b.ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddProvider(logs);
                    logging.AddFilter("GoalFlow", LogLevel.Trace);
                });
            }
        });

    /// <summary>
    /// Envía por <see cref="TestServer.SendAsync"/> para controlar las instancias de cada encabezado: un valor de
    /// <see cref="StringValues"/> por instancia, sin que el cliente HTTP las fusione.
    /// </summary>
    private static async Task<Respuesta> Pedir(
        WebApplicationFactory<Program> fabrica, string ruta, string? token = null, params (string Nombre, string[] Instancias)[] encabezados)
    {
        var contexto = await fabrica.Server.SendAsync(c =>
        {
            c.Request.Method = HttpMethods.Get;
            c.Request.Path = ruta;
            if (token is not null) c.Request.Headers.Authorization = $"Bearer {token}";
            foreach (var (nombre, instancias) in encabezados) c.Request.Headers[nombre] = instancias;
        });

        using var lector = new StreamReader(contexto.Response.Body);
        return new Respuesta((HttpStatusCode)contexto.Response.StatusCode, contexto.Response.Headers, await lector.ReadToEndAsync());
    }

    private string TokenDe(Guid usuario) => TokensDePrueba.Firmar(_k1, usuario);

    // ---- T-01 ----

    // Verde inicial: pasa porque la ruta no existe para nadie. Su control positivo es el 200 de T02_Admin_de_la_liga_recibe_200.
    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    public async Task T01_con_el_interruptor_apagado_o_ausente_un_Admin_valido_recibe_404(string? interruptor)
    {
        await using var fabrica = Anfitrion(interruptor);

        var respuesta = await Pedir(fabrica, Ruta, TokenDe(UsuarioU1));

        Assert.Equal(HttpStatusCode.NotFound, respuesta.Estado);
    }

    [Fact]
    public void T01_los_archivos_de_configuracion_versionados_dejan_el_interruptor_ausente_o_false()
    {
        var directorio = new DirectoryInfo(AppContext.BaseDirectory);
        while (directorio is not null && !Directory.Exists(Path.Combine(directorio.FullName, "GoalFlow.Api"))) directorio = directorio.Parent;
        Assert.NotNull(directorio);

        var archivos = Directory.GetFiles(Path.Combine(directorio.FullName, "GoalFlow.Api"), "*.json", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .ToList();
        Assert.Contains(archivos, f => f.EndsWith("appsettings.json", StringComparison.Ordinal));

        foreach (var archivo in archivos)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(archivo), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("Diagnostico", out var seccion)
                && seccion.TryGetProperty("RedHabilitado", out var valor))
                Assert.True(valor.ValueKind == JsonValueKind.False, $"{Path.GetFileName(archivo)} debe dejar el interruptor en false");
        }

        // El predeterminado es false: appsettings.json lo declara explícitamente.
        using var principal = JsonDocument.Parse(File.ReadAllText(archivos.First(f => f.EndsWith("appsettings.json", StringComparison.Ordinal))));
        Assert.Equal(JsonValueKind.False, principal.RootElement.GetProperty("Diagnostico").GetProperty("RedHabilitado").ValueKind);
    }

    // ---- T-02 ----

    [Fact]
    public async Task T02_slug_desconocido_devuelve_404()
    {
        await using var fabrica = Anfitrion("true");

        var respuesta = await Pedir(fabrica, "/no-existe/admin/diagnostico/red", TokenDe(UsuarioU1));

        Assert.Equal(HttpStatusCode.NotFound, respuesta.Estado);
    }

    [Fact]
    public async Task T02_slug_conocido_sin_token_devuelve_401_con_WWW_Authenticate_Bearer()
    {
        await using var fabrica = Anfitrion("true");

        var respuesta = await Pedir(fabrica, Ruta);

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.Estado);
        Assert.Equal("Bearer", respuesta.Encabezados.WWWAuthenticate.ToString());
    }

    [Fact]
    public async Task T02_Admin_de_otra_liga_devuelve_403()
    {
        await using var fabrica = Anfitrion("true");

        var respuesta = await Pedir(fabrica, Ruta, TokenDe(UsuarioU2));

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.Estado);
    }

    [Fact]
    public async Task T02_Admin_de_la_liga_recibe_200()
    {
        await using var fabrica = Anfitrion("true");

        var respuesta = await Pedir(fabrica, Ruta, TokenDe(UsuarioU1));

        Assert.Equal(HttpStatusCode.OK, respuesta.Estado);
    }
}
