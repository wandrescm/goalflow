using System.Net;
using System.Text.Json.Nodes;
using GoalFlow.Application.IA;
using GoalFlow.Application.Persistencia;
using GoalFlow.Application.Torneos.Contracts;
using GoalFlow.Domain.IA;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using static GoalFlow.Tests.Isolation.EntornoDeLecturasPublicas;

namespace GoalFlow.Tests.Isolation;

/// <summary>
/// Spec 011 T-08 (R-21): el número de comandos de base de cada ruta no depende del número de torneos, fases, partidos, filas de goles,
/// tarjetas ni resúmenes. El contador es <see cref="ContadorDeComandosDelAnfitrion"/>: mide los comandos que EF Core crea e intercepta
/// (incluida la resolución del slug) y no el <c>set_config</c> del tenant, que el interceptor ejecuta directo sobre la conexión. Solo
/// afirma igualdad de comandos; no afirma nada sobre filas, memoria ni tiempo (Spec 011 §4, Cost).
/// </summary>
[Collection(ColeccionDePlanilla.Nombre)]
public class ComandosDeLecturasPublicasTests(BaseDeAislamiento db)
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    /// <summary>Spec 010 C-20: esta prueba guarda cientos de planillas para contar comandos, no prueba el limitador.</summary>
    private static void SinLimiteDeAdmin(IWebHostBuilder b) =>
        b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["LimiteDeFrecuencia:Admin:PorCliente"] = "1000",
            ["LimiteDeFrecuencia:Admin:Global"] = "1000",
        }));

    /// <summary>Una sola vuelta completa de <paramref name="n"/> equipos (método del círculo): n − 1 jornadas sin repetir equipo.</summary>
    private static List<(int Jornada, int Local, int Visitante)> VueltaCompleta(int n)
    {
        var equipos = Enumerable.Range(1, n).ToList();
        var partidos = new List<(int, int, int)>();
        for (var jornada = 1; jornada < n; jornada++)
        {
            for (var i = 0; i < n / 2; i++) partidos.Add((jornada, equipos[i], equipos[n - 1 - i]));
            equipos.Insert(1, equipos[^1]);
            equipos.RemoveAt(equipos.Count - 1);
        }

        return partidos;
    }

    private static string Eq(int i) => $"E{i}";

    private static string Jug(int equipo, int k) => $"J{equipo}-{k}";

    /// <summary>Un torneo de una fase de una vuelta con <paramref name="n"/> equipos, sus jugadores y todos sus partidos.</summary>
    private static JsonObject Rondas(string nombre, int n, int jugadoresPorEquipo = 0) => new()
    {
        ["nombre"] = nombre,
        ["fases"] = new JsonArray(new JsonObject { ["orden"] = 1, ["tipo"] = "TodosContraTodos", ["vueltas"] = 1 }),
        ["equipos"] = new JsonArray([.. Enumerable.Range(1, n).Select(i => (JsonNode)new JsonObject
        {
            ["codigo"] = Eq(i), ["nombre"] = $"Equipo {i}", ["retirado"] = false,
            ["jugadores"] = new JsonArray([.. Enumerable.Range(1, jugadoresPorEquipo).Select(k => (JsonNode)new JsonObject
            {
                ["codigo"] = Jug(i, k), ["nombre"] = $"Jugador {i}-{k}",
            })]),
        })]),
        ["partidos"] = new JsonArray([.. VueltaCompleta(n).Select(m => (JsonNode)new JsonObject
        {
            ["fase"] = 1, ["jornada"] = m.Jornada, ["local"] = Eq(m.Local), ["visitante"] = Eq(m.Visitante), ["cancelado"] = false,
        })]),
    };

    private static async Task JugarTodos(EntornoDeLecturasPublicas e, string liga, string torneo, int n)
    {
        foreach (var m in VueltaCompleta(n))
            await e.E.GuardarConExito(liga, torneo, Eq(m.Local), Eq(m.Visitante),
                new PlanillaJ(0, "Jugado", new(1, 0), TarjetasInformadas: true, GolesSinAutor: new(1, 0)));
    }

    private static async Task<int> Comandos(EntornoDeLecturasPublicas e, string ruta)
    {
        var (estado, _, comandos, _) = await e.Medir(ruta);
        Assert.Equal(HttpStatusCode.OK, estado);
        return comandos;
    }

    [Fact] // T-08: el catálogo, con 1 torneo y con 8 torneos de 8 fases
    public async Task T08_el_catalogo_ejecuta_los_mismos_comandos_con_1_torneo_que_con_8_torneos_de_8_fases()
    {
        await using var e = new EntornoDeLecturasPublicas(db, SinLimiteDeAdmin);
        var (uno, ocho) = (await e.CrearLiga(), await e.CrearLiga());
        await e.E.ImportarConExito(uno, "T1", TorneoMinimo("Torneo 1", (1, 1)));
        for (var t = 1; t <= 8; t++)
            await e.E.ImportarConExito(ocho, $"T{t}", TorneoMinimo($"Torneo {t}", [.. Enumerable.Range(1, 8).Select(f => (f, 1))]));
        Assert.Equal(8, (await e.Cuerpo(Catalogo(ocho)))["torneos"]!.AsArray().Count(t => t!["fases"]!.AsArray().Count == 8));

        var (a, b) = (await Comandos(e, Catalogo(uno)), await Comandos(e, Catalogo(ocho)));

        Console.WriteLine($"T08 catálogo, comandos [1 torneo, 8 torneos de 8 fases]: {a}, {b}");
        Assert.Equal(a, b);
    }

    [Fact] // T-08: la lista, con una fase de 4 equipos y con una vuelta completa de 20 equipos (190 partidos jugados)
    public async Task T08_la_lista_ejecuta_los_mismos_comandos_con_4_equipos_que_con_20_equipos_y_190_partidos()
    {
        await using var e = new EntornoDeLecturasPublicas(db, SinLimiteDeAdmin);
        var liga = await e.CrearLiga();
        await e.E.ImportarConExito(liga, "PEQ", Rondas("Pequeño", 4));
        await e.E.ImportarConExito(liga, "GRA", Rondas("Grande", 20));
        await JugarTodos(e, liga, "PEQ", 4);
        await JugarTodos(e, liga, "GRA", 20);
        Assert.Equal((6, 190), (VueltaCompleta(4).Count, VueltaCompleta(20).Count));
        Assert.Equal(190, (await e.Cuerpo(Partidos(liga, "GRA")))["partidos"]!.AsArray().Count);

        var (a, b) = (await Comandos(e, Partidos(liga, "PEQ")), await Comandos(e, Partidos(liga, "GRA")));

        Console.WriteLine($"T08 lista, comandos [4 equipos, 20 equipos y 190 partidos]: {a}, {b}");
        Assert.Equal(a, b);
    }

    [Fact] // T-08: el detalle, con 0 filas y con 20 de goles, 10 de tarjetas y 3 resúmenes (ambos Jugado y con planilla vigente)
    public async Task T08_el_detalle_ejecuta_los_mismos_comandos_sin_filas_que_con_20_goles_10_tarjetas_y_3_resumenes()
    {
        await using var e = new EntornoDeLecturasPublicas(db, SinLimiteDeAdmin);
        var (liga, tenant) = await e.CrearLigaConTenant();
        await e.E.ImportarConExito(liga, "TD", Rondas("Torneo del detalle", 4, jugadoresPorEquipo: 10));
        // E3–E4: 0–0, sin filas de goles ni de tarjetas y sin resúmenes.
        await e.E.GuardarConExito(liga, "TD", "E3", "E4", new(0, "Jugado", new(0, 0), TarjetasInformadas: true));
        // E1–E2: 10 goles por lado (20 filas), 5 tarjetas por lado (10 filas) y 3 resúmenes.
        await e.E.GuardarConExito(liga, "TD", "E1", "E2", new(0, "Jugado", new(10, 10), TarjetasInformadas: true,
            Goles: [.. Enumerable.Range(1, 2).SelectMany(i => Enumerable.Range(1, 10).Select(k => new GolJ(Jug(i, k), 1)))],
            Tarjetas: [.. Enumerable.Range(1, 2).SelectMany(i => Enumerable.Range(1, 5).Select(k => new TarjetaJ(Jug(i, k), "Amarilla", 1)))]));

        var partido = (await e.PartidosDe(tenant, "TD")).Single(p => (p.Local, p.Visitante) == ("E1", "E2"));
        EntradaCanonica entrada;
        Guid torneoId;
        await using (var scope = e.ScopeDe(tenant))
        {
            await using var transaccion = await scope.ServiceProvider.GetRequiredService<IUnidadDeTrabajo>().IniciarAsync(Ct);
            var datos = (await scope.ServiceProvider.GetRequiredService<ILectorDePartidoParaResumen>().LeerAsync(partido.Id, Ct))!;
            (entrada, torneoId) = (EntradaCanonica.Construir(datos), datos.TorneoId);
        }

        ResumenPartido Fila(string huella, EstadoDeResumen estado, MotivoDeBorrador? motivo, string texto) =>
            new(Guid.CreateVersion7(), tenant, torneoId, partido.Id, 1, huella, OrigenDeResumen.Plantilla, estado, motivo, texto, null, null, null);
        await e.InsertarResumenes(
            tenant,
            Fila(new string('b', 64), EstadoDeResumen.Borrador, MotivoDeBorrador.Presupuesto, "Borrador"),
            Fila(new string('a', 64), EstadoDeResumen.PorVerificar, null, "Huella vieja"),
            Fila(entrada.Huella(), EstadoDeResumen.PorVerificar, null, PlantillasDeResumen.Plantilla(entrada)));
        var cuerpo = await e.Cuerpo(Detalle(liga, "TD", 1, "E1", "E2"));
        Assert.Equal((20, 10), (cuerpo["goles"]!.AsArray().Count, cuerpo["tarjetas"]!.AsArray().Count));
        Assert.NotNull(cuerpo["resumen"]);
        Assert.Empty((await e.Cuerpo(Detalle(liga, "TD", 1, "E3", "E4")))["goles"]!.AsArray());

        var (a, b) = (await Comandos(e, Detalle(liga, "TD", 1, "E3", "E4")), await Comandos(e, Detalle(liga, "TD", 1, "E1", "E2")));

        Console.WriteLine($"T08 detalle, comandos [0 filas, 20 goles + 10 tarjetas + 3 resúmenes]: {a}, {b}");
        Assert.Equal(a, b);
    }
}
