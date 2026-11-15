using System.Data.Common;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using GoalFlow.Infrastructure.Persistencia;
using static GoalFlow.Tests.Isolation.BaseDeAislamiento;
using static GoalFlow.Tests.Isolation.EntornoDePlanilla;

namespace GoalFlow.Tests.Isolation;

/// <summary>Cuenta los comandos que EF Core crea en el anfitrión de pruebas (mismo patrón que <c>ContadorDeComandos</c> de AislamientoEfCoreTests).</summary>
public sealed class ContadorDeComandosDelAnfitrion : DbCommandInterceptor
{
    private int _ejecutados;

    public int Ejecutados => Volatile.Read(ref _ejecutados);

    public void Reiniciar() => Interlocked.Exchange(ref _ejecutados, 0);

    public override DbCommand CommandInitialized(CommandEndEventData eventData, DbCommand result)
    {
        Interlocked.Increment(ref _ejecutados);
        return result;
    }
}

/// <summary>
/// El anfitrión real con el interruptor de los endpoints públicos de estadísticas puesto por la prueba (Spec 009 R-37), y los
/// datos del Apéndice A cargados por la API real: importación con reglamento y planillas con <c>PUT</c>. Datos inventados.
/// </summary>
public sealed class EntornoDeEstadisticas : IAsyncDisposable
{
    public const string ClaveDelInterruptor = "Estadisticas:EndpointsPublicosHabilitados";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly EntornoDePlanilla _entorno;

    /// <param name="interruptor">El valor del interruptor: <c>"true"</c>, <c>"false"</c> o <c>null</c> (ausente).</param>
    public EntornoDeEstadisticas(BaseDeAislamiento db, string? interruptor = "true", Action<IWebHostBuilder>? ajustar = null)
    {
        Comandos = new ContadorDeComandosDelAnfitrion();
        _entorno = new EntornoDePlanilla(db, ajustarAnfitrion: b =>
        {
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { [ClaveDelInterruptor] = interruptor }));
            b.ConfigureTestServices(s => s.ConfigureDbContext<GoalFlowDbContext>(o => o.AddInterceptors(Comandos)));
            ajustar?.Invoke(b);
        });
    }

    public ContadorDeComandosDelAnfitrion Comandos { get; }

    public EntornoDePlanilla Base => _entorno;

    public IServiceProvider Services => _entorno.Services;

    public ValueTask DisposeAsync() => _entorno.DisposeAsync();

    public static string Prefijo() => EntornoDePlanilla.Prefijo();

    private static Guid UsuarioDe(string liga) => liga == "liga-b" ? UsuarioU2 : UsuarioU1;

    // ---- Peticiones ----

    public async Task<HttpResponseMessage> Importar(string liga, string torneo, JsonObject cuerpo)
    {
        var respuesta = await _entorno.Cliente.SendAsync(_entorno.Peticion(
            HttpMethod.Put, $"/{liga}/admin/torneos/{torneo}", cuerpo.ToJsonString(), UsuarioDe(liga)));
        return respuesta;
    }

    public async Task ImportarConExito(string liga, string torneo, JsonObject cuerpo) =>
        Assert.Equal(HttpStatusCode.OK, (await Importar(liga, torneo, cuerpo)).StatusCode);

    public Task<HttpResponseMessage> Guardar(string liga, string torneo, string local, string visitante, PlanillaJ planilla) =>
        _entorno.Guardar(RutaDePlanilla(liga, torneo, 1, local, visitante), planilla, UsuarioDe(liga));

    public async Task GuardarConExito(string liga, string torneo, string local, string visitante, PlanillaJ planilla) =>
        Assert.Equal(HttpStatusCode.OK, (await Guardar(liga, torneo, local, visitante, planilla)).StatusCode);

    /// <summary>Un <c>GET</c> público, sin token.</summary>
    public Task<HttpResponseMessage> Get(string ruta) => _entorno.Cliente.GetAsync(ruta);

    public static string Posiciones(string liga, string torneo, int fase = 1) => $"/{liga}/torneos/{torneo}/fases/{fase}/posiciones";

    public static string Goleadores(string liga, string torneo) => $"/{liga}/torneos/{torneo}/goleadores";

    public static string Valla(string liga, string torneo) => $"/{liga}/torneos/{torneo}/valla";

    public static async Task<(HttpStatusCode Estado, string Texto)> Leer(HttpResponseMessage respuesta) =>
        (respuesta.StatusCode, await respuesta.Content.ReadAsStringAsync());

    // ---- Reglamentos ----

    public static JsonObject WoA() => new()
    {
        ["puntosGanador"] = 3, ["puntosPerdedor"] = 0, ["puntosEmpate"] = 1, ["deduccionPerdedor"] = 0, ["deduccionEmpate"] = 0,
        ["cuentaComoJugado"] = true, ["golesOficialesEnTabla"] = true, ["golesOficialesEnValla"] = true,
        ["golesDeportivosEnGoleadores"] = true, ["tarjetasEnFairPlay"] = true,
    };

    /// <summary>Las reglas de F6: puntos 3-1-0, criterios vacíos no recursivo y WalkOver como «WO-A». Cada sección se puede quitar.</summary>
    public static JsonObject ReglasDeF6(
        string estado = "Provisional", bool puntos = true, bool desempate = true, bool adjudicacion = true)
    {
        var reglas = new JsonObject { ["estado"] = estado };
        if (puntos) reglas["puntos"] = new JsonObject { ["victoria"] = 3, ["empate"] = 1, ["derrota"] = 0 };
        if (desempate) reglas["desempate"] = new JsonObject { ["criterios"] = new JsonArray(), ["directoRecursivo"] = false };
        if (adjudicacion) reglas["adjudicacion"] = new JsonObject { ["WalkOver"] = WoA() };
        return reglas;
    }

    // ---- F6 (Apéndice A) por la API real ----

    public static string Equipo(string p, int i) => $"{p}-E{i}";

    public static string Jugador(string p, string numero) => $"{p}-P{numero}";

    private static readonly (int Equipo, string[] Jugadores)[] PlantillasDeF6 =
    [
        (1, ["11", "12"]), (2, ["21", "22"]), (3, ["31"]), (4, ["41"]), (5, ["51"]), (6, ["61", "62"]),
    ];

    // Los quince partidos de F6 con el equipo local primero, repartidos en cinco jornadas sin repetir equipo.
    private static readonly (int Jornada, int Local, int Visitante)[] PartidosDeF6 =
    [
        (1, 1, 2), (1, 3, 4), (1, 5, 6),
        (2, 1, 3), (2, 2, 5), (2, 4, 6),
        (3, 1, 4), (3, 2, 6), (3, 3, 5),
        (4, 1, 5), (4, 2, 4), (4, 3, 6),
        (5, 1, 6), (5, 2, 3), (5, 4, 5),
    ];

    /// <summary>El cuerpo de la importación de F6; <paramref name="inverso"/> invierte el orden de inserción de equipos, jugadores y partidos (T-19).</summary>
    public static JsonObject CuerpoDeF6(string p, JsonObject? reglamento, bool inverso = false)
    {
        var equipos = PlantillasDeF6.Select(t => new JsonObject
        {
            ["codigo"] = Equipo(p, t.Equipo),
            ["nombre"] = $"Equipo E{t.Equipo}",
            ["retirado"] = false,
            ["jugadores"] = new JsonArray([.. (inverso ? t.Jugadores.Reverse() : t.Jugadores)
                .Select(n => (JsonNode)new JsonObject { ["codigo"] = Jugador(p, n), ["nombre"] = $"Jugador P{n}" })]),
        }).ToList();
        var partidos = PartidosDeF6.Select(m => new JsonObject
        {
            ["fase"] = 1, ["jornada"] = m.Jornada, ["local"] = Equipo(p, m.Local), ["visitante"] = Equipo(p, m.Visitante), ["cancelado"] = false,
        }).ToList();
        if (inverso)
        {
            equipos.Reverse();
            partidos.Reverse();
        }

        var cuerpo = new JsonObject
        {
            ["nombre"] = "Torneo F6",
            ["fases"] = new JsonArray(new JsonObject { ["orden"] = 1, ["tipo"] = "TodosContraTodos", ["vueltas"] = 1 }),
            ["equipos"] = new JsonArray([.. equipos.Select(e => (JsonNode)e)]),
            ["partidos"] = new JsonArray([.. partidos.Select(m => (JsonNode)m)]),
        };
        if (reglamento is not null) cuerpo["reglamento"] = JsonNode.Parse(reglamento.ToJsonString());
        return cuerpo;
    }

    /// <summary>Las planillas de F6, una acción por partido (M12 son dos: Jugado 4–0 y después retirada). Cada una devuelve su respuesta.</summary>
    public IReadOnlyList<Func<Task>> PlanillasDeF6(string liga, string p, bool inverso = false)
    {
        Func<int, int, PlanillaJ, Func<Task>> Guarda = (l, v, planilla) =>
            () => GuardarConExito(liga, p, Equipo(p, l), Equipo(p, v), planilla);
        string J(string n) => Jugador(p, n);

        List<Func<Task>> acciones =
        [
            Guarda(1, 2, new(0, "Jugado", new(2, 1), TarjetasInformadas: true,
                Goles: [new(J("11"), 2), new(J("21"), 1)], Tarjetas: [new(J("11"), "Amarilla", 1)])),                       // M1
            Guarda(1, 3, new(0, "Jugado", new(1, 0), TarjetasInformadas: true, GolesSinAutor: new(1, 0))),                   // M2
            Guarda(1, 4, new(0, "Jugado", new(0, 0), TarjetasInformadas: false)),                                           // M3
            Guarda(1, 5, new(0, "Jugado", new(3, 1), TarjetasInformadas: false,
                Goles: [new(J("12"), 2), new(J("51"), 1)], GolesSinAutor: new(1, 0))),                                      // M4
            Guarda(1, 6, new(0, "Jugado", new(1, 1), TarjetasInformadas: true,
                Goles: [new(J("62"), 1)], Autogoles: [new(J("61"), 1)])),                                                   // M5
            Guarda(2, 3, new(0, "Jugado", new(2, 2), TarjetasInformadas: true, Goles: [new(J("21"), 2)], GolesSinAutor: new(0, 2))), // M6
            Guarda(2, 4, new(0, "Jugado", new(1, 0), TarjetasInformadas: true, Goles: [new(J("22"), 1)])),                  // M7
            Guarda(2, 5, new(0, "Jugado", new(0, 2), TarjetasInformadas: true, Goles: [new(J("51"), 2)])),                  // M8
            Guarda(2, 6, new(0, "Jugado", new(1, 1), TarjetasInformadas: true, Goles: [new(J("21"), 1), new(J("61"), 1)])), // M9
            Guarda(3, 4, new(0, "Adjudicado", Oficial: new(3, 0), Motivo: "WalkOver")),                                     // M10
            Guarda(3, 5, new(0, "Suspendido", new(1, 0), Motivo: "Disturbios", TarjetasInformadas: true,
                Goles: [new(J("31"), 1)], Tarjetas: [new(J("31"), "Roja", 1)])),                                            // M11
            async () =>                                                                                                      // M12
            {
                await Guarda(3, 6, new(0, "Jugado", new(4, 0), TarjetasInformadas: true, Goles: [new(J("31"), 4)]))();
                await Guarda(3, 6, Retirada(1))();
            },
        ];
        if (inverso) acciones.Reverse();
        return acciones;
    }

    /// <summary>Importa F6 y guarda todas sus planillas. Con <paramref name="reglamento"/> nulo, sin reglas.</summary>
    public async Task CargarF6(string liga, string p, JsonObject? reglamento, bool inverso = false)
    {
        await ImportarConExito(liga, p, CuerpoDeF6(p, reglamento, inverso));
        foreach (var accion in PlanillasDeF6(liga, p, inverso)) await accion();
    }
}
