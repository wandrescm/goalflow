using System.Globalization;
using System.Net;
using GoalFlow.Api.Hosting;
using GoalFlow.Application.Ligas;
using GoalFlow.Domain.Ligas;
using GoalFlow.Tests.Isolation;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GoalFlow.Tests.Api;

/// <summary>
/// Spec 010 T-12 a T-18. La aplicación real <b>sin base de datos</b>: límites pequeños por configuración, una cadena de conexión
/// inerte (nada se conecta jamás) y un doble de <see cref="IResolutorDeSlug"/> que cuenta llamadas y devuelve null.
/// </summary>
public class LimiteDeFrecuenciaTests
{
    private const string Origen = "https://app.goalflow.example";
    private const string RutaPublica = "/no/existe";
    private const string RutaAdmin = "/x/admin/sesion";

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

    private static WebApplicationFactory<Program> Anfitrion(
        int publicaPorCliente = 120, int publicaGlobal = 600, int adminPorCliente = 60, int adminGlobal = 120,
        string? origenPermitido = null, ResolutorQueCuenta? resolutor = null, CapturaDeLogs? logs = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("LimiteDeFrecuencia:Publica:PorCliente", publicaPorCliente.ToString(CultureInfo.InvariantCulture));
            b.UseSetting("LimiteDeFrecuencia:Publica:Global", publicaGlobal.ToString(CultureInfo.InvariantCulture));
            b.UseSetting("LimiteDeFrecuencia:Admin:PorCliente", adminPorCliente.ToString(CultureInfo.InvariantCulture));
            b.UseSetting("LimiteDeFrecuencia:Admin:Global", adminGlobal.ToString(CultureInfo.InvariantCulture));
            // Inerte: el DbContext solo se construye, nunca se conecta; el doble del resolutor corta antes de cualquier consulta.
            b.UseSetting("ConnectionStrings:GoalFlow", "Host=127.0.0.1;Port=1;Database=prueba;Username=prueba");
            if (origenPermitido is not null) b.UseSetting("Cors:AllowedOrigins:0", origenPermitido);

            b.ConfigureTestServices(servicios =>
            {
                if (resolutor is null) return;
                servicios.RemoveAll<IResolutorDeSlug>();
                servicios.AddSingleton<IResolutorDeSlug>(resolutor);
            });

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

    private static Task<HttpResponseMessage> Pedir(HttpClient cliente, string ruta, string? ip, params (string Nombre, string Valor)[] otros)
    {
        var peticion = new HttpRequestMessage(HttpMethod.Get, ruta);
        if (ip is not null) peticion.Headers.Add("CF-Connecting-IP", ip);
        foreach (var (nombre, valor) in otros) peticion.Headers.Add(nombre, valor);
        return cliente.SendAsync(peticion);
    }

    private static async Task<HttpStatusCode> Estado(HttpClient cliente, string ruta, string? ip, params (string Nombre, string Valor)[] otros)
    {
        using var respuesta = await Pedir(cliente, ruta, ip, otros);
        return respuesta.StatusCode;
    }

    private static async Task Enviar(HttpClient cliente, string ruta, string? ip, int cuantas, HttpStatusCode esperado)
    {
        for (var i = 1; i <= cuantas; i++)
            Assert.Equal(esperado, await Estado(cliente, ruta, ip));
    }

    private static async Task Agotar(HttpClient cliente, string ruta, string? ip, int cuota)
    {
        await Enviar(cliente, ruta, ip, cuota, HttpStatusCode.NotFound);
        Assert.Equal(HttpStatusCode.TooManyRequests, await Estado(cliente, ruta, ip));
    }

    // ---- T-12 ----

    [Fact]
    public async Task T12_los_valores_por_defecto_son_exactamente_Publica_120_600_y_Admin_60_120()
    {
        await using var fabrica = new WebApplicationFactory<Program>();

        var opciones = fabrica.Services.GetRequiredService<IOptions<OpcionesDeLimiteDeFrecuencia>>().Value;

        Assert.Equal(120, opciones.Publica.PorCliente);
        Assert.Equal(600, opciones.Publica.Global);
        Assert.Equal(60, opciones.Admin.PorCliente);
        Assert.Equal(120, opciones.Admin.Global);
    }

    public static TheoryData<string, string> LimitesInvalidos
    {
        get
        {
            var datos = new TheoryData<string, string>();
            foreach (var clave in new[] { "Publica:PorCliente", "Publica:Global", "Admin:PorCliente", "Admin:Global" })
                foreach (var valor in new[] { "0", "-1", "no-es-un-entero" })
                    datos.Add(clave, valor);
            return datos;
        }
    }

    [Theory]
    [MemberData(nameof(LimitesInvalidos))]
    public async Task T12_un_limite_invalido_detiene_el_arranque(string clave, string valor)
    {
        // Spec 010 C-22: sin WebApplicationFactory. Esa fábrica ejecuta el Program real, cuyo app.Run() dispone el host al fallar la
        // validación mientras la fábrica resuelve servicios en otro hilo: la carrera da ObjectDisposedException en lugar del error.
        // Un host genérico en un solo hilo, con la misma extensión de producción y solo esta clave, no tiene esa carrera.
        var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { [$"LimiteDeFrecuencia:{clave}"] = valor });
        builder.Services.AddGoalFlowLimiteDeFrecuencia();
        using var host = builder.Build();

        var error = await Assert.ThrowsAnyAsync<Exception>(() => host.StartAsync());

        Assert.IsNotType<ObjectDisposedException>(error); // nunca es un resultado válido
        Assert.Contains($"LimiteDeFrecuencia:{clave}", error.ToString());
        if (valor == "no-es-un-entero")
        {
            Assert.IsType<InvalidOperationException>(error); // el error de conversión del enlace de configuración
            Assert.Contains($"LimiteDeFrecuencia:{clave}", error.Message);
        }
        else
            Assert.IsType<OptionsValidationException>(error);
    }

    [Fact] // Control positivo de T-12 (a): el mismo montaje con límites válidos arranca y se detiene; los fallos de la Theory son del valor
    public async Task T12_con_limites_validos_el_mismo_host_arranca()
    {
        var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["LimiteDeFrecuencia:Publica:PorCliente"] = "5",
            ["LimiteDeFrecuencia:Publica:Global"] = "5",
            ["LimiteDeFrecuencia:Admin:PorCliente"] = "5",
            ["LimiteDeFrecuencia:Admin:Global"] = "5",
        });
        builder.Services.AddGoalFlowLimiteDeFrecuencia();
        using var host = builder.Build();

        await host.StartAsync();
        await host.StopAsync();
    }

    [Fact] // T-12 (b): la composición real de la API usa la extensión que la Theory valida
    public async Task T12_la_composicion_de_la_API_registra_el_validador_de_limites()
    {
        await using var fabrica = new WebApplicationFactory<Program>(); // configuración válida: arranca sin fallar, no hay carrera

        var validadores = fabrica.Services.GetServices<IValidateOptions<OpcionesDeLimiteDeFrecuencia>>();

        Assert.Contains(validadores, v => v is ValidadorDeLimiteDeFrecuencia);
    }

    // ---- T-13 ----

    [Fact]
    public async Task T13_health_nunca_se_limita_ni_consume_permisos()
    {
        await using var fabrica = Anfitrion(publicaPorCliente: 3);
        var cliente = fabrica.CreateClient();
        const string ip = "192.0.2.50";

        await Enviar(cliente, "/health", ip, 10, HttpStatusCode.OK);
        await Agotar(cliente, RutaPublica, ip, cuota: 3); // la cuota entera sigue intacta tras 10 /health

        await Enviar(cliente, "/health", ip, 5, HttpStatusCode.OK); // y con la clase agotada, /health responde 200
    }

    // Spec 010 v5, T-13 (b): verde inicial; su rojo lo da el mutante ME de la auditoría (/health compartiendo la cuota global de publica).
    [Fact]
    public async Task T13b_health_no_consume_ni_queda_limitado_por_la_cuota_global_de_publica()
    {
        await using var fabrica = Anfitrion(publicaPorCliente: 10, publicaGlobal: 3);
        var cliente = fabrica.CreateClient();

        // Paso 1: cinco /health del mismo cliente, todas 200 (más que el global de publica).
        await Enviar(cliente, "/health", "192.0.2.80", 5, HttpStatusCode.OK);

        // Paso 2: tres públicas de tres direcciones, aceptadas (404): el global de publica seguía intacto.
        Assert.Equal(HttpStatusCode.NotFound, await Estado(cliente, RutaPublica, "192.0.2.81"));
        Assert.Equal(HttpStatusCode.NotFound, await Estado(cliente, RutaPublica, "192.0.2.82"));
        Assert.Equal(HttpStatusCode.NotFound, await Estado(cliente, RutaPublica, "192.0.2.83"));

        // Paso 3: la cuarta pública agota el global de publica: 429.
        Assert.Equal(HttpStatusCode.TooManyRequests, await Estado(cliente, RutaPublica, "192.0.2.84"));

        // Paso 4: con publica agotada, /health sigue respondiendo 200.
        await Enviar(cliente, "/health", "192.0.2.80", 3, HttpStatusCode.OK);
    }

    // ---- T-14 ----

    [Fact]
    public async Task T14_el_rechazo_de_un_origen_permitido_es_429_legible_con_Retry_After_y_CORS()
    {
        await using var fabrica = Anfitrion(publicaPorCliente: 1, origenPermitido: Origen);
        var cliente = fabrica.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, await Estado(cliente, RutaPublica, "192.0.2.60", ("Origin", Origen)));

        using var rechazada = await Pedir(cliente, RutaPublica, "192.0.2.60", ("Origin", Origen));

        Assert.Equal(HttpStatusCode.TooManyRequests, rechazada.StatusCode);
        Assert.Equal("""{"error":"limite_de_frecuencia"}""", await rechazada.Content.ReadAsStringAsync());
        Assert.Equal("application/json", rechazada.Content.Headers.ContentType?.MediaType);
        Assert.True(rechazada.Headers.TryGetValues("Retry-After", out var reintento));
        Assert.True(int.TryParse(Assert.Single(reintento), NumberStyles.None, CultureInfo.InvariantCulture, out var segundos) && segundos >= 1);
        Assert.True(rechazada.Headers.TryGetValues("Access-Control-Allow-Origin", out var origen));
        Assert.Equal(Origen, Assert.Single(origen));
        Assert.True(rechazada.Headers.TryGetValues("Access-Control-Expose-Headers", out var expuestos));
        Assert.Contains("Retry-After", string.Join(",", expuestos));
    }

    // ---- T-15 ----

    [Theory]
    [InlineData("/x/equipos")]
    [InlineData(RutaAdmin)]
    public async Task T15_una_peticion_rechazada_no_llega_al_resolutor_de_slug(string ruta)
    {
        var resolutor = new ResolutorQueCuenta();
        await using var fabrica = Anfitrion(publicaPorCliente: 1, adminPorCliente: 1, resolutor: resolutor);
        var cliente = fabrica.CreateClient();
        const string ip = "192.0.2.70";

        Assert.Equal(HttpStatusCode.NotFound, await Estado(cliente, ruta, ip));
        Assert.Equal(1, resolutor.Llamadas); // control: aceptada, sí llega al resolutor

        await Enviar(cliente, ruta, ip, 5, HttpStatusCode.TooManyRequests);

        Assert.Equal(1, resolutor.Llamadas);
    }

    // ---- T-16 ----

    [Fact]
    public async Task T16_cadena_por_cliente_y_luego_global_cuenta_exactamente_los_permisos_de_R17()
    {
        await using var fabrica = Anfitrion(publicaPorCliente: 10, publicaGlobal: 100);
        var cliente = fabrica.CreateClient();

        // X manda 50: 10 aceptadas y 40 rechazadas por su propio límite.
        var estadosDeX = new List<HttpStatusCode>();
        for (var i = 0; i < 50; i++) estadosDeX.Add(await Estado(cliente, RutaPublica, "198.51.100.10"));
        Assert.Equal(10, estadosDeX.Count(e => e == HttpStatusCode.NotFound));
        Assert.Equal(40, estadosDeX.Count(e => e == HttpStatusCode.TooManyRequests));
        Assert.All(estadosDeX.Take(10), e => Assert.Equal(HttpStatusCode.NotFound, e));

        // C1 a C9 mandan 10 cada uno, todas aceptadas: 90 en total (X solo gastó 10 permisos globales).
        for (var c = 1; c <= 9; c++)
            await Enviar(cliente, RutaPublica, $"192.0.2.{c}", 10, HttpStatusCode.NotFound);

        // La primera petición de C10 agota el global (100): 429.
        Assert.Equal(HttpStatusCode.TooManyRequests, await Estado(cliente, RutaPublica, "192.0.2.10"));
    }

    // ---- T-17 ----

    [Fact]
    public async Task T17a_un_cliente_agotado_no_afecta_a_otro_de_la_misma_clase()
    {
        await using var fabrica = Anfitrion(publicaPorCliente: 3);
        var cliente = fabrica.CreateClient();

        await Agotar(cliente, RutaPublica, "198.51.100.10", cuota: 3);

        Assert.Equal(HttpStatusCode.NotFound, await Estado(cliente, RutaPublica, "192.0.2.20"));
    }

    [Fact]
    public async Task T17b_agotar_publica_no_rechaza_admin_y_agotar_admin_no_rechaza_publica()
    {
        await using var fabrica = Anfitrion(publicaPorCliente: 10, publicaGlobal: 3, adminPorCliente: 10, adminGlobal: 3, resolutor: new ResolutorQueCuenta());
        var cliente = fabrica.CreateClient();

        await Agotar(cliente, RutaPublica, "192.0.2.1", cuota: 3); // publica agotada (global 3)
        Assert.Equal(HttpStatusCode.NotFound, await Estado(cliente, RutaAdmin, "192.0.2.1"));

        await Agotar(cliente, RutaAdmin, "192.0.2.2", cuota: 2); // admin: 1 ya gastada arriba, quedan 2 y la siguiente se rechaza
        Assert.Equal(HttpStatusCode.TooManyRequests, await Estado(cliente, RutaPublica, "192.0.2.3")); // publica sigue agotada
    }

    [Fact]
    public async Task T17b_reverso_agotar_admin_no_rechaza_publica()
    {
        await using var fabrica = Anfitrion(publicaPorCliente: 10, publicaGlobal: 3, adminPorCliente: 10, adminGlobal: 3, resolutor: new ResolutorQueCuenta());
        var cliente = fabrica.CreateClient();

        await Agotar(cliente, RutaAdmin, "192.0.2.1", cuota: 3);

        Assert.Equal(HttpStatusCode.NotFound, await Estado(cliente, RutaPublica, "192.0.2.1"));
    }

    [Fact]
    public async Task T17c_otros_encabezados_falsificados_no_mueven_la_particion()
    {
        await using var fabrica = Anfitrion(publicaPorCliente: 2);
        var cliente = fabrica.CreateClient();
        const string x = "198.51.100.10";
        await Agotar(cliente, RutaPublica, x, cuota: 2);

        // La forma capturada más valores falsificados: X sigue siendo X.
        var falsificada = await Estado(cliente, RutaPublica, x,
            ("X-Forwarded-For", "203.0.113.66, 198.51.100.10, 104.22.17.40, 10.200.0.5"),
            ("X-Real-IP", "203.0.113.66"),
            ("True-Client-IP", "203.0.113.66"));
        Assert.Equal(HttpStatusCode.TooManyRequests, falsificada);

        // Otro cliente (CF-Connecting-IP distinta) con el X-Forwarded-For y el True-Client-IP de X: se acepta.
        var otro = await Estado(cliente, RutaPublica, "192.0.2.20",
            ("X-Forwarded-For", "198.51.100.10, 104.22.17.40, 10.200.0.5"),
            ("True-Client-IP", x));
        Assert.Equal(HttpStatusCode.NotFound, otro);
    }

    [Fact]
    public async Task T17d_las_peticiones_sin_encabezado_comparten_sin_ip_y_no_afectan_a_los_demas()
    {
        await using var fabrica = Anfitrion(publicaPorCliente: 2);
        var cliente = fabrica.CreateClient();

        await Agotar(cliente, RutaPublica, ip: null, cuota: 2);

        Assert.Equal(HttpStatusCode.TooManyRequests, await Estado(cliente, RutaPublica, ip: null));
        Assert.Equal(HttpStatusCode.NotFound, await Estado(cliente, RutaPublica, "198.51.100.10"));
    }

    // Spec 010 v5, T-17 (e): verde inicial; su rojo lo da el mutante MF de la auditoría (la clave por cliente sin la clase).
    [Fact]
    public async Task T17e_la_clase_forma_parte_de_la_clave_por_cliente()
    {
        await using var fabrica = Anfitrion(
            publicaPorCliente: 1, publicaGlobal: 10, adminPorCliente: 1, adminGlobal: 10, resolutor: new ResolutorQueCuenta());
        var cliente = fabrica.CreateClient();
        const string x = "198.51.100.10";
        const string y = "192.0.2.20";

        // Paso 1: X agota su cuota pública (1).
        Assert.Equal(HttpStatusCode.NotFound, await Estado(cliente, RutaPublica, x));
        // Paso 2: la cuota de X en la clase admin es otra partición: se acepta.
        Assert.Equal(HttpStatusCode.NotFound, await Estado(cliente, RutaAdmin, x));
        // Paso 3: Y entra primero por admin: se acepta.
        Assert.Equal(HttpStatusCode.NotFound, await Estado(cliente, RutaAdmin, y));
        // Paso 4: y su cuota pública es otra partición: se acepta.
        Assert.Equal(HttpStatusCode.NotFound, await Estado(cliente, RutaPublica, y));
    }

    // ---- T-18 ----

    [Fact]
    public async Task T18_los_rechazos_no_dejan_ninguna_entrada_de_log_con_la_IP_ni_la_clave()
    {
        const string centinela = "203.0.113.201";
        var logs = new CapturaDeLogs();
        await using var fabrica = Anfitrion(publicaPorCliente: 1, logs: logs);
        var cliente = fabrica.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, await Estado(cliente, RutaPublica, centinela));
        await Enviar(cliente, RutaPublica, centinela, 3, HttpStatusCode.TooManyRequests);

        Assert.NotEmpty(logs.Entradas); // control: la captura de logs funciona con los niveles de producción
        Assert.All(logs.Entradas, entrada =>
        {
            Assert.DoesNotContain(centinela, entrada);
            Assert.DoesNotContain($"publica|{centinela}", entrada, StringComparison.OrdinalIgnoreCase);
        });
    }
}
