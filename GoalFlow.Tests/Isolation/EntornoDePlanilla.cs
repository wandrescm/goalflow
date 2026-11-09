using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Npgsql;
using static GoalFlow.Tests.Isolation.BaseDeAislamiento;

namespace GoalFlow.Tests.Isolation;

/// <summary>
/// Colección propia, sin paralelismo y con su propia base: Spec 006 T-05 mide todas las tablas del schema y ningún otro
/// anfitrión debe estar vivo mientras tanto (C-31).
/// </summary>
[CollectionDefinition(Nombre, DisableParallelization = true)]
public class ColeccionDePlanilla : ICollectionFixture<BaseDeAislamiento>
{
    public const string Nombre = "Planilla";
}

// ---- Contrato de la petición de la planilla (Spec 006 R-09) y de la importación (Spec 005 R-14) ----

public sealed record MarcadorJ(int? Local, int? Visitante);

public sealed record GolJ(string Jugador, int Cantidad);

public sealed record TarjetaJ(string Jugador, string Color, int Cantidad);

public sealed record PlanillaJ(
    int VersionEsperada,
    string Estado,
    MarcadorJ? Marcador = null,
    MarcadorJ? Oficial = null,
    string? Motivo = null,
    string? MotivoRectificacion = null,
    bool? TarjetasInformadas = null,
    List<GolJ>? Goles = null,
    List<GolJ>? Autogoles = null,
    MarcadorJ? GolesSinAutor = null,
    List<TarjetaJ>? Tarjetas = null);

public sealed record JugadorJ(string Codigo, string Nombre);

public sealed record EquipoJ(string Codigo, string Nombre, bool Retirado, List<JugadorJ> Jugadores);

public sealed record FaseJ(int Orden, string Tipo, int Vueltas);

public sealed record PartidoJ(int Fase, int Jornada, string Local, string Visitante, string? ProgramadoEn, bool Cancelado);

public sealed record TorneoJ(string Nombre, List<FaseJ> Fases, List<EquipoJ> Equipos, List<PartidoJ> Partidos);

/// <summary>
/// La API real contra el contenedor con el JWKS en memoria y el ciclo post-guardado apagado, más los ayudantes que usan las
/// pruebas de la planilla. Datos inventados: torneo con prefijo único, <c>EQ-01</c> a <c>EQ-04</c>, <c>J-11</c> a <c>J-43</c>.
/// </summary>
public sealed class EntornoDePlanilla : IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly LlaveDePrueba Llave = new();
    private static int _anfitrionesVivos;

    private readonly BaseDeAislamiento _db;
    private readonly WebApplicationFactory<Program> _fabrica;

    public EntornoDePlanilla(
        BaseDeAislamiento db, CapturaDeLogs? logs = null, Action<IWebHostBuilder>? ajustarAnfitrion = null,
        bool cicloHabilitado = false)
    {
        _db = db;
        _fabrica = AnfitrionDeAdmin.Crear(db, new FuenteDeJwksDePrueba(Llave), logs).WithWebHostBuilder(b =>
        {
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Mensajeria:CicloHabilitado"] = cicloHabilitado ? "true" : "false" }));
            ajustarAnfitrion?.Invoke(b);
        });
        Cliente = _fabrica.CreateClient();
        Interlocked.Increment(ref _anfitrionesVivos);
    }

    public HttpClient Cliente { get; }

    public IServiceProvider Services => _fabrica.Services;

    /// <summary>Anfitriones de esta clase sin liberar en el proceso.</summary>
    public static int AnfitrionesVivos => Volatile.Read(ref _anfitrionesVivos);

    public async ValueTask DisposeAsync()
    {
        Cliente.Dispose();
        await _fabrica.DisposeAsync();
        Interlocked.Decrement(ref _anfitrionesVivos);
    }

    public static string Prefijo() => Guid.NewGuid().ToString("N")[..6];

    // ---- Datos del torneo ----

    public static string Equipo(string p, int i) => $"{p}-EQ-{i:00}";

    /// <summary>Código del jugador <paramref name="n"/> (1 a 3) del equipo <paramref name="equipo"/> (1 a 4): J-11, J-12, J-13, J-21...</summary>
    public static string Jugador(string p, int equipo, int n) => $"{p}-J-{equipo}{n}";

    /// <summary>Una fase, cuatro equipos con tres jugadores cada uno y seis partidos; el primero es EQ-01 contra EQ-02.</summary>
    public static TorneoJ Torneo(string p)
    {
        var equipos = Enumerable.Range(1, 4)
            .Select(e => new EquipoJ(
                Equipo(p, e), $"Equipo {NombreDe(e)}", false,
                [.. Enumerable.Range(1, 3).Select(j => new JugadorJ(Jugador(p, e, j), $"Jugador {NombreDe(e)}{j}"))]))
            .ToList();
        List<PartidoJ> partidos =
        [
            new(1, 1, Equipo(p, 1), Equipo(p, 2), "2026-11-08T15:00:00-05:00", false),
            new(1, 1, Equipo(p, 3), Equipo(p, 4), null, false),
            new(1, 2, Equipo(p, 1), Equipo(p, 3), null, false),
            new(1, 2, Equipo(p, 2), Equipo(p, 4), null, false),
            new(1, 3, Equipo(p, 1), Equipo(p, 4), null, false),
            new(1, 3, Equipo(p, 2), Equipo(p, 3), null, false),
        ];
        return new TorneoJ("Torneo Uno", [new FaseJ(1, "TodosContraTodos", 1)], equipos, partidos);
    }

    private static string NombreDe(int i) => i switch { 1 => "Uno", 2 => "Dos", 3 => "Tres", _ => "Cuatro" };

    // ---- Peticiones ----

    public static string RutaDePlanilla(string liga, string torneo, int fase, string local, string visitante) =>
        $"/{liga}/admin/torneos/{torneo}/fases/{fase}/partidos/{local}/{visitante}/planilla";

    public static string RutaDelPrimerPartido(string p) => RutaDePlanilla("liga-a", p, 1, Equipo(p, 1), Equipo(p, 2));

    private static string Token(Guid usuario) => TokensDePrueba.Firmar(Llave, usuario);

    public HttpRequestMessage Peticion(HttpMethod metodo, string ruta, string? cuerpo = null, Guid? usuario = null)
    {
        var peticion = new HttpRequestMessage(metodo, ruta);
        if (cuerpo is not null) peticion.Content = new StringContent(cuerpo, Encoding.UTF8, "application/json");
        if (usuario is { } u) peticion.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token(u));
        return peticion;
    }

    public Task<HttpResponseMessage> Importar(string codigo, TorneoJ fixture, string liga = "liga-a") =>
        Cliente.SendAsync(Peticion(
            HttpMethod.Put, $"/{liga}/admin/torneos/{codigo}", JsonSerializer.Serialize(fixture, Json),
            liga == "liga-b" ? UsuarioU2 : UsuarioU1));

    /// <summary>Importa el torneo y exige 200: la preparación de cada prueba.</summary>
    public async Task<string> Preparar(TorneoJ? fixture = null)
    {
        var p = Prefijo();
        var respuesta = await Importar(p, fixture ?? Torneo(p));
        Assert.Equal(System.Net.HttpStatusCode.OK, respuesta.StatusCode);
        return p;
    }

    public Task<HttpResponseMessage> Guardar(string ruta, PlanillaJ planilla, Guid? usuario = null) =>
        GuardarTexto(ruta, JsonSerializer.Serialize(planilla, Json), usuario);

    public Task<HttpResponseMessage> GuardarTexto(string ruta, string cuerpo, Guid? usuario = null) =>
        Cliente.SendAsync(Peticion(HttpMethod.Put, ruta, cuerpo, usuario ?? UsuarioU1));

    public Task<HttpResponseMessage> Leer(string ruta, Guid? usuario = null) =>
        Cliente.SendAsync(Peticion(HttpMethod.Get, ruta, null, usuario ?? UsuarioU1));

    public static async Task<JsonDocument> Cuerpo(HttpResponseMessage respuesta) =>
        JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());

    /// <summary>La transformación real de R-16: quita <c>version</c> y la envía como <c>versionEsperada</c>.</summary>
    public static string DeGetAPut(JsonDocument get)
    {
        var miembros = new Dictionary<string, JsonElement>();
        foreach (var propiedad in get.RootElement.EnumerateObject())
            if (propiedad.Name != "version") miembros[propiedad.Name] = propiedad.Value;
        var salida = new Dictionary<string, object?>(miembros.ToDictionary(m => m.Key, m => (object?)m.Value))
        {
            ["versionEsperada"] = get.RootElement.GetProperty("version").GetInt32(),
        };
        return JsonSerializer.Serialize(salida, Json);
    }

    // ---- Planillas de ejemplo (partido 1: EQ-01 local contra EQ-02 visitante) ----

    /// <summary>3–1: J-11 dos goles, autogol de J-21 (visitante, se acredita al local), J-22 un gol, dos tarjetas.</summary>
    public static PlanillaJ Jugado3a1(string p, int versionEsperada) => new(
        versionEsperada, "Jugado", new(3, 1), null, null, null, true,
        [new(Jugador(p, 1, 1), 2), new(Jugador(p, 2, 2), 1)],
        [new(Jugador(p, 2, 1), 1)],
        null,
        [new(Jugador(p, 1, 2), "Amarilla", 1), new(Jugador(p, 2, 2), "Roja", 1)]);

    /// <summary>La corrección de C-05: el gol de J-22 pasa a goles sin autor del visitante.</summary>
    public static PlanillaJ Corregido3a1(string p, int versionEsperada) => Jugado3a1(p, versionEsperada) with
    {
        Goles = [new(Jugador(p, 1, 1), 2)],
        GolesSinAutor = new(0, 1),
    };

    /// <summary>La retirada de la planilla de un partido guardado por error (R-11 d, C-12).</summary>
    public static PlanillaJ Retirada(int versionEsperada, string motivo = "PartidoEquivocado") =>
        new(versionEsperada, "Programado", MotivoRectificacion: motivo);

    public static PlanillaJ Vacia(int versionEsperada) => new(versionEsperada, "Programado");

    // ---- Medición ----

    private async Task<object?> Escalar(string sql)
    {
        await using var conexion = new NpgsqlConnection(_db.CadenaSuperusuario);
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand(sql, conexion);
        return await comando.ExecuteScalarAsync();
    }

    public async Task<long> Contar(string sql) => Convert.ToInt64(await Escalar(sql));

    public async Task<string?> Texto(string sql) => (await Escalar(sql)) as string;

    private const string PartidoDe = """
        select pa.id from goalflow.partidos pa
        join goalflow.torneos t on t.id = pa.torneo_id
        join goalflow.fases f on f.id = pa.fase_id
        join goalflow.equipos_torneo l on l.id = pa.local_id join goalflow.equipos el on el.id = l.equipo_id
        join goalflow.equipos_torneo v on v.id = pa.visitante_id join goalflow.equipos ev on ev.id = v.equipo_id
        """;

    /// <summary>El id del partido por su clave natural.</summary>
    public string FiltroDePartido(string p, int local, int visitante) => $"""
        {PartidoDe}
        where t.codigo = '{p}' and f.orden = 1 and el.codigo = '{Equipo(p, local)}' and ev.codigo = '{Equipo(p, visitante)}'
        """;

    public Task<long> Planillas(string p, int local = 1, int visitante = 2) =>
        Contar($"select count(*) from goalflow.planillas where partido_id = ({FiltroDePartido(p, local, visitante)})");

    public Task<long> Goles(string p, int local = 1, int visitante = 2) => Contar($"""
        select count(*) from goalflow.goles g join goalflow.planillas pl on pl.id = g.planilla_id
        where pl.partido_id = ({FiltroDePartido(p, local, visitante)})
        """);

    public Task<long> Tarjetas(string p, int local = 1, int visitante = 2) => Contar($"""
        select count(*) from goalflow.tarjetas c join goalflow.planillas pl on pl.id = c.planilla_id
        where pl.partido_id = ({FiltroDePartido(p, local, visitante)})
        """);

    public Task<string?> EstadoDelPartido(string p, int local = 1, int visitante = 2) =>
        Texto($"select estado from goalflow.partidos where id = ({FiltroDePartido(p, local, visitante)})");

    public Task<long> VersionDelPartido(string p, int local = 1, int visitante = 2) =>
        Contar($"select version from goalflow.partidos where id = ({FiltroDePartido(p, local, visitante)})");

    /// <summary>Foto de todas las tablas del schema como superusuario: filas y multiconjunto de xmin (Spec 005 C-15).</summary>
    public async Task<Dictionary<string, string[]>> Foto()
    {
        await using var conexion = new NpgsqlConnection(_db.CadenaSuperusuario);
        await conexion.OpenAsync();
        var tablas = new List<string>();
        await using (var catalogo = new NpgsqlCommand(
            "select c.relname from pg_class c join pg_namespace n on n.oid = c.relnamespace where n.nspname = 'goalflow' and c.relkind = 'r' order by 1",
            conexion))
        await using (var lector = await catalogo.ExecuteReaderAsync())
            while (await lector.ReadAsync()) tablas.Add(lector.GetString(0));

        var foto = new Dictionary<string, string[]>();
        foreach (var tabla in tablas)
        {
            await using var comando = new NpgsqlCommand($"select xmin::text from goalflow.\"{tabla}\"", conexion);
            await using var lector = await comando.ExecuteReaderAsync();
            var versiones = new List<string>();
            while (await lector.ReadAsync()) versiones.Add(lector.GetString(0));
            foto[tabla] = [.. versiones.Order(StringComparer.Ordinal)];
        }

        Assert.True(foto.Count >= 12, "La foto debe cubrir todas las tablas del schema goalflow, incluidas las de la planilla");
        Assert.Contains("planillas", foto.Keys);
        return foto;
    }

    public static void FotosIguales(Dictionary<string, string[]> antes, Dictionary<string, string[]> despues)
    {
        Assert.Equal(antes.Keys.Order().ToArray(), despues.Keys.Order().ToArray());
        foreach (var tabla in antes.Keys)
            Assert.True(antes[tabla].SequenceEqual(despues[tabla]), $"La tabla {tabla} cambió (filas o xmin)");
    }
}
