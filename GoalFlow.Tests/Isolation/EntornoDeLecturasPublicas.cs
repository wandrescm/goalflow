using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using GoalFlow.Application.IA;
using GoalFlow.Application.Ligas;
using GoalFlow.Application.Persistencia;
using GoalFlow.Application.Tenancy;
using GoalFlow.Domain.IA;
using GoalFlow.Domain.Ligas;
using GoalFlow.Domain.Tenancy;
using GoalFlow.Infrastructure.Ligas;
using Npgsql;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using static GoalFlow.Tests.Isolation.BaseDeAislamiento;

namespace GoalFlow.Tests.Isolation;

/// <summary>
/// El anfitrión real y los ayudantes de las pruebas de lecturas públicas (Spec 011). Envuelve <see cref="EntornoDeEstadisticas"/> sin
/// modificarlo: el interruptor de estadísticas queda ausente, porque estas rutas no dependen de él (R-27). Cada prueba que necesita un
/// cuerpo exacto crea su propia liga, para que los datos de las demás pruebas de la colección no entren en el catálogo.
/// </summary>
public sealed partial class EntornoDeLecturasPublicas : IAsyncDisposable
{
    public static readonly JsonSerializerOptions Json = EntornoDeEstadisticas.Json;

    private readonly BaseDeAislamiento _db;

    public EntornoDeLecturasPublicas(BaseDeAislamiento db, Action<IWebHostBuilder>? ajustar = null)
    {
        _db = db;
        Resolutor = new ContadorDeResolutor();
        E = new EntornoDeEstadisticas(db, interruptor: null, b =>
        {
            b.ConfigureTestServices(servicios =>
            {
                // Decora al resolutor real: cuenta las llamadas y delega (sus comandos siguen contando para el interceptor de EF Core).
                var original = servicios.Single(d => d.ServiceType == typeof(IResolutorDeSlug));
                servicios.Remove(original);
                servicios.AddScoped<IResolutorDeSlug>(sp =>
                    new ResolutorQueCuenta(ActivatorUtilities.CreateInstance<ResolutorDeSlug>(sp), Resolutor));
            });
            ajustar?.Invoke(b);
        });
    }

    public EntornoDeEstadisticas E { get; }

    public ContadorDeResolutor Resolutor { get; }

    public ContadorDeComandosDelAnfitrion Comandos => E.Comandos;

    public sealed class ContadorDeResolutor
    {
        private int _llamadas;

        public int Llamadas => Volatile.Read(ref _llamadas);

        public void Registrar() => Interlocked.Increment(ref _llamadas);
    }

    private sealed class ResolutorQueCuenta(IResolutorDeSlug interno, ContadorDeResolutor contador) : IResolutorDeSlug
    {
        public Task<Liga?> ResolverAsync(string slug, CancellationToken cancellationToken)
        {
            contador.Registrar();
            return interno.ResolverAsync(slug, cancellationToken);
        }
    }

    /// <summary>Una petición <c>GET</c> con el recuento de comandos de base (desde el inicio de la petición) y las llamadas al resolutor.</summary>
    public async Task<(HttpStatusCode Estado, string Texto, int Comandos, int Resoluciones)> Medir(string ruta, string? autorizacion = null)
    {
        var antes = Resolutor.Llamadas;
        Comandos.Reiniciar();
        using var peticion = new HttpRequestMessage(HttpMethod.Get, ruta);
        if (autorizacion is not null) peticion.Headers.TryAddWithoutValidation("Authorization", autorizacion);
        using var respuesta = await E.Base.Cliente.SendAsync(peticion);
        var texto = await respuesta.Content.ReadAsStringAsync();
        return (respuesta.StatusCode, texto, Comandos.Ejecutados, Resolutor.Llamadas - antes);
    }

    // Valores mal formados de la Spec 011 T-13 (a). El segmento vacío no llega al endpoint: el enrutador responde antes.
    public static readonly string[] OrdenesMalFormados = ["0", "-1", "+1", "1.0", "%201", "x", "99999999999"];

    public static readonly string[] CodigosMalFormados = ["a.b", "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx", "a%20b"];

    public ValueTask DisposeAsync() => E.DisposeAsync();

    [GeneratedRegex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    public static partial Regex PatronDeGuid();

    // ---- Ligas propias de la prueba ----

    /// <summary>Crea una liga nueva con U1 como Admin y devuelve su slug. Datos inventados.</summary>
    public async Task<string> CrearLiga(string nombre = "Liga de prueba") => (await CrearLigaConTenant(nombre)).Slug;

    public async Task<(string Slug, TenantId Tenant)> CrearLigaConTenant(string nombre = "Liga de prueba")
    {
        var (id, slug) = (Guid.NewGuid(), $"liga-{Guid.NewGuid():N}"[..14]);
        await _db.EjecutarComoSuperusuario($"""
            insert into goalflow.ligas (id, slug, nombre) values ('{id}', '{slug}', '{nombre}');
            insert into goalflow.admins_liga (user_id, tenant_id, rol) values ('{UsuarioU1}', '{id}', 'admin');
            """);
        return (slug, new TenantId(id));
    }

    // ---- Acceso directo a los puertos, por el registro real de DI del anfitrión ----

    /// <summary>Un scope del anfitrión con el tenant fijado, como lo deja el endpoint tras resolver el slug.</summary>
    public AsyncServiceScope ScopeDe(TenantId tenant)
    {
        var scope = E.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().Establecer(tenant);
        return scope;
    }

    /// <summary>Inserta filas de resumen con el adaptador real, en una transacción de la unidad de trabajo, y confirma.</summary>
    public async Task InsertarResumenes(TenantId tenant, params ResumenPartido[] filas)
    {
        await using var scope = ScopeDe(tenant);
        await using var transaccion = await scope.ServiceProvider.GetRequiredService<IUnidadDeTrabajo>().IniciarAsync(default);
        var almacen = scope.ServiceProvider.GetRequiredService<IAlmacenDeResumenes>();
        foreach (var fila in filas) Assert.True(await almacen.InsertarAsync(fila, default));
        await transaccion.ConfirmarAsync(default);
    }

    /// <summary>Los partidos de la fase 1 de un torneo con su clave natural, leídos como superusuario (solo para armar pruebas).</summary>
    public async Task<List<(Guid Id, string Local, string Visitante)>> PartidosDe(TenantId tenant, string torneo)
    {
        await using var conexion = new NpgsqlConnection(_db.CadenaSuperusuario);
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand($"""
            select pa.id, el.codigo, ev.codigo from goalflow.partidos pa
            join goalflow.torneos t on t.id = pa.torneo_id
            join goalflow.fases f on f.id = pa.fase_id
            join goalflow.equipos_torneo l on l.id = pa.local_id join goalflow.equipos el on el.id = l.equipo_id
            join goalflow.equipos_torneo v on v.id = pa.visitante_id join goalflow.equipos ev on ev.id = v.equipo_id
            where pa.tenant_id = '{tenant.Value}' and t.codigo = '{torneo}' and f.orden = 1
            """, conexion);
        await using var lector = await comando.ExecuteReaderAsync();
        var partidos = new List<(Guid, string, string)>();
        while (await lector.ReadAsync()) partidos.Add((lector.GetGuid(0), lector.GetString(1), lector.GetString(2)));
        return partidos;
    }

    // ---- Peticiones ----

    public Task<HttpResponseMessage> Get(string ruta) => E.Get(ruta);

    public async Task<string> Texto(string ruta, HttpStatusCode esperado = HttpStatusCode.OK)
    {
        var (estado, texto) = await EntornoDeEstadisticas.Leer(await E.Get(ruta));
        Assert.True(estado == esperado, $"Se esperaba {esperado} para {ruta} y llegó {estado}: {texto}");
        return texto;
    }

    public async Task<JsonNode> Cuerpo(string ruta, HttpStatusCode esperado = HttpStatusCode.OK) =>
        JsonNode.Parse(await Texto(ruta, esperado))!;

    // ---- Rutas ----

    public static string Catalogo(string liga) => $"/{liga}/torneos";

    public static string Partidos(string liga, string torneo, int fase = 1) => $"/{liga}/torneos/{torneo}/fases/{fase}/partidos";

    public static string Detalle(string liga, string torneo, int fase, string local, string visitante) =>
        $"/{liga}/torneos/{torneo}/fases/{fase}/partidos/{local}/{visitante}";

    // ---- Cuerpos esperados: el texto JSON completo, con las mismas opciones que la API ----

    public static string Serializar(object valor) => JsonSerializer.Serialize(valor, Json);

    /// <summary>Compara el texto JSON completo, no campos sueltos (Spec 011 §6.1).</summary>
    public static void IgualesLiteralmente(object esperado, string real) => Assert.Equal(Serializar(esperado), real);

    // ---- Importación ----

    /// <summary>Un torneo con las fases dadas, dos equipos y un partido en la primera fase. Datos inventados.</summary>
    public static JsonObject TorneoMinimo(string nombre, params (int Orden, int Vueltas)[] fases) => new()
    {
        ["nombre"] = nombre,
        ["fases"] = new JsonArray([.. fases.Select(f => (JsonNode)new JsonObject
        {
            ["orden"] = f.Orden, ["tipo"] = "TodosContraTodos", ["vueltas"] = f.Vueltas,
        })]),
        ["equipos"] = new JsonArray(
            new JsonObject { ["codigo"] = "E1", ["nombre"] = "Equipo Uno", ["retirado"] = false, ["jugadores"] = new JsonArray() },
            new JsonObject { ["codigo"] = "E2", ["nombre"] = "Equipo Dos", ["retirado"] = false, ["jugadores"] = new JsonArray() }),
        ["partidos"] = new JsonArray(new JsonObject
        {
            ["fase"] = fases[0].Orden, ["jornada"] = 1, ["local"] = "E1", ["visitante"] = "E2", ["cancelado"] = false,
        }),
    };

    // ---- El fixture de T-02: una fase de una vuelta con un partido en cada estado ----

    public const string TorneoT02 = "T2";

    public static readonly (string Codigo, string Nombre)[] EquiposT02 =
    [
        ("E1", "Equipo Uno"), ("E2", "Equipo Dos"), ("E3", "Equipo Tres"), ("E4", "Equipo Cuatro"),
        ("e5", "Equipo Cinco"), ("E6", "Equipo Seis"), ("E7", "Equipo Siete"), ("E8", "Equipo Ocho"),
    ];

    // El código «e5» en minúscula hace que el orden ordinal difiera del de cualquier cultura (R-06).
    private static readonly (int Jornada, string Local, string Visitante, string? Hora)[] PartidosT02 =
    [
        (1, "E1", "E2", "2026-11-20T20:00:00-05:00"),
        (1, "E3", "E4", null),
        (1, "e5", "E6", null),
        (1, "E7", "E8", null),
        (2, "E1", "E3", "2026-11-22T10:30:00-05:00"),
        (2, "E2", "E4", null),
        (2, "e5", "E7", "2026-11-22T09:00:00-05:00"),
        (2, "E6", "E8", null),
    ];

    /// <summary>La importación de T-02. <paramref name="cancelarElUltimo"/> cancela E6–E8 (C-32); E7–E8 llega cancelado desde el principio.</summary>
    public static JsonObject CuerpoT02(bool cancelarElUltimo, bool inverso)
    {
        var equipos = EquiposT02.Select(e => new JsonObject
        {
            ["codigo"] = e.Codigo, ["nombre"] = e.Nombre, ["retirado"] = false, ["jugadores"] = new JsonArray(),
        }).ToList();
        var partidos = PartidosT02.Select(m => new JsonObject
        {
            ["fase"] = 1, ["jornada"] = m.Jornada, ["local"] = m.Local, ["visitante"] = m.Visitante, ["programadoEn"] = m.Hora,
            ["cancelado"] = (m.Local, m.Visitante) is ("E7", "E8") || (cancelarElUltimo && (m.Local, m.Visitante) is ("E6", "E8")),
        }).ToList();
        if (inverso)
        {
            equipos.Reverse();
            partidos.Reverse();
        }

        return new JsonObject
        {
            ["nombre"] = "Torneo Dos",
            ["fases"] = new JsonArray(new JsonObject { ["orden"] = 1, ["tipo"] = "TodosContraTodos", ["vueltas"] = 1 }),
            ["equipos"] = new JsonArray([.. equipos.Select(e => (JsonNode)e)]),
            ["partidos"] = new JsonArray([.. partidos.Select(m => (JsonNode)m)]),
        };
    }

    /// <summary>
    /// Importa T-02 y guarda sus planillas: E1–E2 de 2–0 (v1) a 1–1 (v2); E3–E4 3–1; E5–E6 sin planilla; E7–E8 cancelado; E1–E3 suspendido
    /// 1–0; E2–E4 adjudicado sin marcador; E5–E7 adjudicado con marcador; E6–E8 jugado, retirado y después cancelado por la importación.
    /// </summary>
    public async Task CargarT02(string liga, bool inverso = false)
    {
        await E.ImportarConExito(liga, TorneoT02, CuerpoT02(cancelarElUltimo: false, inverso));
        Task G(string local, string visitante, PlanillaJ planilla) => E.GuardarConExito(liga, TorneoT02, local, visitante, planilla);

        List<Func<Task>> acciones =
        [
            async () =>
            {
                await G("E1", "E2", new(0, "Jugado", new(2, 0), TarjetasInformadas: true, GolesSinAutor: new(2, 0)));
                await G("E1", "E2", new(1, "Jugado", new(1, 1), TarjetasInformadas: true, GolesSinAutor: new(1, 1)));
            },
            () => G("E3", "E4", new(0, "Jugado", new(3, 1), TarjetasInformadas: false, GolesSinAutor: new(3, 1))),
            () => G("E1", "E3", new(0, "Suspendido", new(1, 0), Motivo: "Disturbios", TarjetasInformadas: true, GolesSinAutor: new(1, 0))),
            () => G("E2", "E4", new(0, "Adjudicado", Oficial: new(3, 0), Motivo: "WalkOver")),
            () => G("e5", "E7", new(0, "Adjudicado", new(2, 1), new(3, 0), "Sancion", TarjetasInformadas: true, GolesSinAutor: new(2, 1))),
            async () =>
            {
                await G("E6", "E8", new(0, "Jugado", new(1, 0), TarjetasInformadas: true, GolesSinAutor: new(1, 0)));
                await G("E6", "E8", EntornoDePlanilla.Retirada(1));
            },
        ];
        if (inverso) acciones.Reverse();
        foreach (var accion in acciones) await accion();

        // C-32: la importación cancela un partido cuya planilla vigente es un retiro.
        await E.ImportarConExito(liga, TorneoT02, CuerpoT02(cancelarElUltimo: true, inverso));
    }

    // ---- Fugas (Spec 011 T-12) ----

    /// <summary>Ningún <c>Guid</c>, el <c>sub</c> de los Admins de prueba ni los miembros internos de la Spec 011 T-12.</summary>
    public static void SinFugas(string texto)
    {
        Assert.DoesNotMatch(PatronDeGuid(), texto);
        foreach (var prohibido in new[]
        {
            UsuarioU1.ToString(), UsuarioU2.ToString(), "fk_", "ux_", "ak_", "23503", "23505", "constraint", "xmin", "Npgsql", "guardadoPor",
            "\"version\":", "motivoRectificacion", "huella", "motivoBorrador", "\"modelo\":", "versionPrompt", "llamadaId",
        })
            Assert.DoesNotContain(prohibido, texto, StringComparison.OrdinalIgnoreCase);
    }
}
