using System.Net;
using System.Text.Json.Nodes;
using static GoalFlow.Tests.Isolation.EntornoDeLecturasPublicas;

namespace GoalFlow.Tests.Isolation;

/// <summary>
/// Spec 011 commit 2: <c>GET /{slug}/torneos</c> (R-01, R-02) por la API real contra PostgreSQL 17 como goalflow_app. Cada prueba crea su
/// propia liga: el catálogo de <c>liga-a</c> lo comparten las demás pruebas de la colección y no admite un cuerpo exacto.
/// </summary>
[Collection(ColeccionDePlanilla.Nombre)]
public class CatalogoPublicoTests(BaseDeAislamiento db)
{
    private static object Fase(int orden, int vueltas) => new { orden, tipo = "TodosContraTodos", vueltas };

    [Fact] // T-01
    public async Task T01_el_catalogo_devuelve_exactamente_el_cuerpo_de_R01_con_los_torneos_por_codigo_y_las_fases_por_orden()
    {
        await using var e = new EntornoDeLecturasPublicas(db);
        var liga = await e.CrearLiga("Liga Catalogo");
        // b1 se importa primero y sus fases llegan en desorden: el cuerpo no depende del orden de inserción.
        await e.E.ImportarConExito(liga, "b1", TorneoMinimo("Torneo Be", (2, 2), (1, 1)));
        await e.E.ImportarConExito(liga, "A1", TorneoMinimo("Torneo A", (1, 1), (2, 2)));

        var esperado = new
        {
            liga = new { slug = liga, nombre = "Liga Catalogo" },
            torneos = new[]
            {
                new { codigo = "A1", nombre = "Torneo A", fases = new[] { Fase(1, 1), Fase(2, 2) } },
                new { codigo = "b1", nombre = "Torneo Be", fases = new[] { Fase(1, 1), Fase(2, 2) } },
            },
        };
        IgualesLiteralmente(esperado, await e.Texto(Catalogo(liga)));
    }

    [Fact] // T-01: una liga sin torneos
    public async Task T01_una_liga_sin_torneos_responde_200_con_torneos_vacio()
    {
        await using var e = new EntornoDeLecturasPublicas(db);
        var liga = await e.CrearLiga("Liga Vacia");

        IgualesLiteralmente(new { liga = new { slug = liga, nombre = "Liga Vacia" }, torneos = Array.Empty<object>() }, await e.Texto(Catalogo(liga)));
    }

    [Fact] // T-01 / R-02: un slug inexistente, con el cuerpo de cualquier 404 público
    public async Task T01_un_slug_inexistente_responde_404_con_el_cuerpo_de_una_ruta_desconocida()
    {
        await using var e = new EntornoDeLecturasPublicas(db);
        var (_, deRutaDesconocida) = await EntornoDeEstadisticas.Leer(await e.Get("/liga-a/ruta-que-no-existe/x"));

        var (estado, texto) = await EntornoDeEstadisticas.Leer(await e.Get(Catalogo("no-existe-nunca")));

        Assert.Equal(HttpStatusCode.NotFound, estado);
        Assert.Equal(deRutaDesconocida, texto);
    }

    [Fact] // T-01 "Must fail when: an order is culture-sensitive": con a2, B1 y A3 el orden ordinal difiere del de cualquier cultura
    public async Task T01_los_torneos_se_ordenan_por_codigo_con_comparacion_ordinal()
    {
        await using var e = new EntornoDeLecturasPublicas(db);
        var liga = await e.CrearLiga();
        foreach (var codigo in new[] { "a2", "B1", "A3" })
            await e.E.ImportarConExito(liga, codigo, TorneoMinimo($"Torneo {codigo}", (1, 1)));

        var cuerpo = await e.Cuerpo(Catalogo(liga));

        Assert.Equal(["A3", "B1", "a2"], cuerpo["torneos"]!.AsArray().Select(t => (string)t!["codigo"]!).ToArray());
    }

    [Fact] // T-12 (catálogo): sin Guid, sin sub y sin miembros internos
    public async Task T12_el_catalogo_no_filtra_ningun_identificador_ni_miembro_interno()
    {
        await using var e = new EntornoDeLecturasPublicas(db);
        var liga = await e.CrearLiga();
        await e.E.ImportarConExito(liga, "T1", TorneoMinimo("Torneo Uno", (1, 1), (2, 2)));

        SinFugas(await e.Texto(Catalogo(liga)));
    }

    [Fact] // T-15 (catálogo): torneos y fases importados en orden inverso dan el mismo cuerpo, byte a byte
    public async Task T15_el_catalogo_no_depende_del_orden_de_insercion()
    {
        await using var e = new EntornoDeLecturasPublicas(db);
        var (directa, inversa) = (await e.CrearLiga("Liga Orden"), await e.CrearLiga("Liga Orden"));
        await e.E.ImportarConExito(directa, "A1", TorneoMinimo("Torneo A", (1, 1), (2, 2)));
        await e.E.ImportarConExito(directa, "b1", TorneoMinimo("Torneo Be", (1, 1), (2, 2)));
        await e.E.ImportarConExito(inversa, "b1", TorneoMinimo("Torneo Be", (2, 2), (1, 1)));
        await e.E.ImportarConExito(inversa, "A1", TorneoMinimo("Torneo A", (2, 2), (1, 1)));

        var (a, b) = (await e.Cuerpo(Catalogo(directa)), await e.Cuerpo(Catalogo(inversa)));

        // El slug es lo único que debe diferir: el resto del texto es idéntico.
        Assert.Equal(a["torneos"]!.ToJsonString(), b["torneos"]!.ToJsonString());
        Assert.Equal(a.ToJsonString().Replace(directa, "SLUG"), b.ToJsonString().Replace(inversa, "SLUG"));
    }
}
