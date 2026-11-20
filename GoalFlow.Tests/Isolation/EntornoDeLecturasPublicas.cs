using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
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
        E = new EntornoDeEstadisticas(db, interruptor: null, ajustar);
    }

    public EntornoDeEstadisticas E { get; }

    public ContadorDeComandosDelAnfitrion Comandos => E.Comandos;

    public ValueTask DisposeAsync() => E.DisposeAsync();

    [GeneratedRegex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    public static partial Regex PatronDeGuid();

    // ---- Ligas propias de la prueba ----

    /// <summary>Crea una liga nueva con U1 como Admin y devuelve su slug. Datos inventados.</summary>
    public async Task<string> CrearLiga(string nombre = "Liga de prueba")
    {
        var (id, slug) = (Guid.NewGuid(), $"liga-{Guid.NewGuid():N}"[..14]);
        await _db.EjecutarComoSuperusuario($"""
            insert into goalflow.ligas (id, slug, nombre) values ('{id}', '{slug}', '{nombre}');
            insert into goalflow.admins_liga (user_id, tenant_id, rol) values ('{UsuarioU1}', '{id}', 'admin');
            """);
        return slug;
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
