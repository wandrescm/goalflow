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

    // ---- T-03 a T-08 (con el interruptor en "true") ----

    private static string[] Cadenas(JsonElement lista) => [.. lista.EnumerateArray().Select(e => e.GetString()!)];

    private static JsonElement Encabezados(Respuesta respuesta) => respuesta.Json.GetProperty("antes").GetProperty("encabezados");

    [Fact]
    public async Task T03_la_captura_es_anterior_a_los_encabezados_reenviados_y_separa_las_entradas_en_orden()
    {
        await using var fabrica = Anfitrion("true");

        var respuesta = await Pedir(fabrica, Ruta, TokenDe(UsuarioU1),
            ("X-Forwarded-For", ["198.51.100.1, 203.0.113.7"]), ("CF-Connecting-IP", ["203.0.113.9"]));

        Assert.Equal(HttpStatusCode.OK, respuesta.Estado);
        var cuerpo = respuesta.Json;
        Assert.Equal("10.0.0.1", cuerpo.GetProperty("antes").GetProperty("ipDeConexion").GetString());
        var encabezados = Encabezados(respuesta);
        Assert.Equal(["198.51.100.1, 203.0.113.7"], Cadenas(encabezados.GetProperty("X-Forwarded-For")));
        Assert.Equal(["203.0.113.9"], Cadenas(encabezados.GetProperty("CF-Connecting-IP")));
        Assert.Equal(2, encabezados.EnumerateObject().Count());
        Assert.Equal(["198.51.100.1", "203.0.113.7"], Cadenas(cuerpo.GetProperty("xForwardedForEntradas")));
        Assert.Equal("203.0.113.7", cuerpo.GetProperty("despues").GetProperty("ipRemota").GetString());
    }

    // Caracterización extra del handoff §3 (la ruta se compara sin distinguir mayúsculas), no es una prueba de la Spec.
    [Fact]
    public async Task Handoff_la_ruta_se_captura_sin_distinguir_mayusculas()
    {
        await using var fabrica = Anfitrion("true");

        var respuesta = await Pedir(fabrica, "/liga-a/ADMIN/Diagnostico/RED", TokenDe(UsuarioU1), ("X-Forwarded-For", ["203.0.113.7"]));

        Assert.Equal(HttpStatusCode.OK, respuesta.Estado);
        Assert.Equal("10.0.0.1", respuesta.Json.GetProperty("antes").GetProperty("ipDeConexion").GetString());
        Assert.Equal(["203.0.113.7"], Cadenas(Encabezados(respuesta).GetProperty("X-Forwarded-For")));
    }

    [Fact]
    public async Task T04_dos_instancias_de_X_Forwarded_For_salen_como_dos_elementos_en_orden()
    {
        await using var fabrica = Anfitrion("true");

        var respuesta = await Pedir(fabrica, Ruta, TokenDe(UsuarioU1), ("X-Forwarded-For", ["198.51.100.1", "203.0.113.7"]));

        Assert.Equal(HttpStatusCode.OK, respuesta.Estado);
        Assert.Equal(["198.51.100.1", "203.0.113.7"], Cadenas(Encabezados(respuesta).GetProperty("X-Forwarded-For")));
        Assert.Equal(["198.51.100.1", "203.0.113.7"], Cadenas(respuesta.Json.GetProperty("xForwardedForEntradas")));
    }

    [Fact]
    public async Task T04_un_valor_de_600_caracteres_vuelve_cortado_a_512()
    {
        await using var fabrica = Anfitrion("true");

        var respuesta = await Pedir(fabrica, Ruta, TokenDe(UsuarioU1), ("X-Real-IP", [new string('a', 600)]));

        Assert.Equal(HttpStatusCode.OK, respuesta.Estado);
        Assert.Equal([new string('a', 512)], Cadenas(Encabezados(respuesta).GetProperty("X-Real-IP")));
    }

    [Fact]
    public async Task T04_nueve_instancias_vuelven_como_ocho_las_primeras_en_orden()
    {
        await using var fabrica = Anfitrion("true");
        string[] nueve = [.. Enumerable.Range(1, 9).Select(i => $"192.0.2.{i}")];

        var respuesta = await Pedir(fabrica, Ruta, TokenDe(UsuarioU1), ("X-Forwarded-For", nueve));

        Assert.Equal(HttpStatusCode.OK, respuesta.Estado);
        Assert.Equal(nueve[..8], Cadenas(Encabezados(respuesta).GetProperty("X-Forwarded-For")));
    }

    [Fact]
    public async Task T05_la_respuesta_lleva_no_store_y_los_encabezados_que_no_llegaron_no_aparecen()
    {
        await using var fabrica = Anfitrion("true");

        var respuesta = await Pedir(fabrica, Ruta, TokenDe(UsuarioU1), ("X-Forwarded-For", ["203.0.113.7"]));

        Assert.Equal(HttpStatusCode.OK, respuesta.Estado);
        Assert.Contains("no-store", respuesta.Encabezados.CacheControl.ToString());
        var encabezados = Encabezados(respuesta);
        Assert.Equal(["X-Forwarded-For"], encabezados.EnumerateObject().Select(p => p.Name));
        Assert.False(encabezados.TryGetProperty("CF-Connecting-IP", out _));
        Assert.All(encabezados.EnumerateObject(), p => Assert.NotEmpty(p.Value.EnumerateArray()));
    }

    [Fact]
    public async Task T06_el_cuerpo_no_contiene_el_token_la_cookie_ni_otros_encabezados_ni_ids()
    {
        await using var fabrica = Anfitrion("true");
        var token = TokenDe(UsuarioU1);

        var respuesta = await Pedir(fabrica, Ruta, token,
            ("Cookie", ["s=abc"]), ("X-Personalizado", ["xyz"]),
            ("X-Forwarded-For", ["198.51.100.1, 203.0.113.7"]), ("CF-Connecting-IP", ["203.0.113.9"]));

        Assert.Equal(HttpStatusCode.OK, respuesta.Estado);
        Assert.Contains("203.0.113.9", respuesta.Cuerpo); // control positivo: los valores de la lista sí pasan
        Assert.DoesNotContain(token, respuesta.Cuerpo);
        Assert.DoesNotContain("abc", respuesta.Cuerpo);
        Assert.DoesNotContain("xyz", respuesta.Cuerpo);
        Assert.DoesNotContain(UsuarioU1.ToString(), respuesta.Cuerpo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(LigaA.Value.ToString(), respuesta.Cuerpo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Authorization", respuesta.Cuerpo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Cookie", respuesta.Cuerpo, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task T07_ninguna_entrada_de_log_contiene_una_IP_ni_un_valor_de_encabezado()
    {
        const string centinela = "203.0.113.201";
        var logs = new CapturaDeLogs();
        await using var fabrica = Anfitrion("true", logs);

        var respuesta = await Pedir(fabrica, Ruta, TokenDe(UsuarioU1),
            [.. EncabezadosDeLaLista.Select(nombre => (nombre, new[] { centinela }))]);

        Assert.Equal(HttpStatusCode.OK, respuesta.Estado);
        Assert.Contains(centinela, respuesta.Cuerpo); // control positivo: el valor pasó por la captura
        Assert.NotEmpty(logs.Entradas); // control: la captura de logs funciona con los niveles de producción
        Assert.All(logs.Entradas, entrada =>
        {
            Assert.DoesNotContain(centinela, entrada);
            Assert.DoesNotContain("10.0.0.1", entrada);
        });
    }

    private static readonly string[] EncabezadosDeLaLista =
    [
        "X-Forwarded-For", "X-Forwarded-Proto", "Forwarded", "X-Real-IP",
        "CF-Connecting-IP", "CF-Connecting-IPv6", "CF-Pseudo-IPv4", "True-Client-IP",
    ];

    // Verde inicial: pasa porque la captura no toca otras rutas; su rojo lo dará la auditoría (Fase B).
    [Fact]
    public async Task T08_con_el_interruptor_en_true_otra_ruta_responde_igual_que_con_el_interruptor_en_false()
    {
        (string, string[])[] falsificados =
        [
            ("X-Forwarded-For", ["203.0.113.66"]), ("X-Real-IP", ["203.0.113.66"]),
            ("CF-Connecting-IP", ["203.0.113.66"]), ("True-Client-IP", ["203.0.113.66"]),
        ];

        await using var encendido = Anfitrion("true");
        var conInterruptor = await Pedir(encendido, "/liga-a/equipos", null, falsificados);
        await using var apagado = Anfitrion("false");
        var sinInterruptor = await Pedir(apagado, "/liga-a/equipos", null, falsificados);

        Assert.Equal(HttpStatusCode.OK, sinInterruptor.Estado);
        Assert.Equal(sinInterruptor.Estado, conInterruptor.Estado);
        Assert.Equal(sinInterruptor.Cuerpo, conInterruptor.Cuerpo);
    }
}
