using System.Net;
using System.Text.Json.Nodes;
using static GoalFlow.Tests.Isolation.EntornoDeLecturasPublicas;

namespace GoalFlow.Tests.Isolation;

/// <summary>
/// Spec 011 commit 5: el detalle público de un partido por su clave natural, solo los hechos (R-08 a R-13), por la API real contra
/// PostgreSQL 17 como goalflow_app. Los cuerpos esperados se arman aquí, literales, desde la Spec.
/// </summary>
[Collection(ColeccionDePlanilla.Nombre)]
public class DetalleDePartidoPublicoTests(BaseDeAislamiento db)
{
    public const string Torneo3 = "T3";

    private static readonly (string Codigo, string Nombre)[] Equipos3 =
    [
        ("E1", "Equipo Uno"), ("E2", "Equipo Dos"), ("E3", "Equipo Tres"), ("E4", "Equipo Cuatro"),
        ("E5", "Equipo Cinco"), ("E6", "Equipo Seis"), ("E7", "Equipo Siete"), ("E8", "Equipo Ocho"),
    ];

    // Dos jugadores con el mismo nombre («Jugador Beto», P12 y P13) para comprobar el desempate por código.
    private static readonly Dictionary<string, (string Nombre, string[] Jugadores)> Plantillas = new()
    {
        ["E1"] = ("Equipo Uno", ["P11", "P12", "P13", "P14"]),
        ["E2"] = ("Equipo Dos", ["P21", "P22"]),
    };

    private static readonly Dictionary<string, string> NombresDeJugadores = new()
    {
        ["P11"] = "Jugador Ana", ["P12"] = "Jugador Beto", ["P13"] = "Jugador Beto", ["P14"] = "Jugador Dora",
        ["P21"] = "Jugador Cami", ["P22"] = "Jugador Dani",
    };

    private static JsonObject CuerpoDeT3(bool inverso)
    {
        var equipos = Equipos3.Select(e => new JsonObject
        {
            ["codigo"] = e.Codigo, ["nombre"] = e.Nombre, ["retirado"] = false,
            ["jugadores"] = new JsonArray([.. (Plantillas.TryGetValue(e.Codigo, out var p) ? p.Jugadores : [])
                .Select(j => (JsonNode)new JsonObject { ["codigo"] = j, ["nombre"] = NombresDeJugadores[j] })]),
        }).ToList();
        (int Jornada, string Local, string Visitante, string? Hora, bool Cancelado)[] datos =
        [
            (1, "E1", "E2", null, false), (1, "E3", "E4", null, false), (1, "E5", "E6", null, false), (1, "E7", "E8", null, false),
            (2, "E1", "E3", null, false), (2, "E2", "E4", "2026-11-21T15:00:00-05:00", false), (2, "E5", "E7", null, true),
        ];
        var partidos = datos.Select(m => new JsonObject
        {
            ["fase"] = 1, ["jornada"] = m.Jornada, ["local"] = m.Local, ["visitante"] = m.Visitante,
            ["programadoEn"] = m.Hora, ["cancelado"] = m.Cancelado,
        }).ToList();
        if (inverso)
        {
            equipos.Reverse();
            partidos.Reverse();
        }

        return new JsonObject
        {
            ["nombre"] = "Torneo Tres",
            ["fases"] = new JsonArray(new JsonObject { ["orden"] = 1, ["tipo"] = "TodosContraTodos", ["vueltas"] = 1 }),
            ["equipos"] = new JsonArray([.. equipos.Select(e => (JsonNode)e)]),
            ["partidos"] = new JsonArray([.. partidos.Select(m => (JsonNode)m)]),
        };
    }

    /// <summary>Importa T-03 y guarda sus planillas (con <paramref name="inverso"/>, todo en orden inverso).</summary>
    private static async Task CargarT3(EntornoDeLecturasPublicas e, string liga, bool inverso = false)
    {
        await e.E.ImportarConExito(liga, Torneo3, CuerpoDeT3(inverso));
        Task G(string local, string visitante, PlanillaJ planilla) => e.E.GuardarConExito(liga, Torneo3, local, visitante, planilla);

        List<Func<Task>> acciones =
        [
            // E1–E2 6–3: E1 = P11 ×2 + P12 + P13 + autogol de P22 (visitante) + 1 sin autor; E2 = P21 ×2 + autogol de P14 (local).
            // Las filas llegan en desorden para que el orden del cuerpo no dependa de la inserción.
            () => G("E1", "E2", new(0, "Jugado", new(6, 3), TarjetasInformadas: true,
                Goles: [new("P13", 1), new("P21", 2), new("P12", 1), new("P11", 2)],
                Autogoles: [new("P22", 1), new("P14", 1)],
                GolesSinAutor: new(1, 0),
                Tarjetas: [new("P22", "Roja", 1), new("P21", "Amarilla", 2), new("P11", "Azul", 1), new("P12", "Azul", 1), new("P11", "Amarilla", 1)])),
            () => G("E3", "E4", new(0, "Jugado", new(1, 0), TarjetasInformadas: false, GolesSinAutor: new(1, 0))),
            () => G("E5", "E6", new(0, "Adjudicado", new(2, 1), new(3, 0), "Sancion", TarjetasInformadas: true, GolesSinAutor: new(2, 1))),
            () => G("E7", "E8", new(0, "Adjudicado", Oficial: new(3, 0), Motivo: "WalkOver")),
            () => G("E1", "E3", new(0, "Suspendido", new(1, 0), Motivo: "Clima", TarjetasInformadas: true, Goles: [new("P11", 1)])),
        ];
        if (inverso) acciones.Reverse();
        foreach (var accion in acciones) await accion();
    }

    private static object Eq(string codigo, string nombre) => new { codigo, nombre };

    private static object? Marc((int Local, int Visitante)? m) => m is { } v ? new { local = v.Local, visitante = v.Visitante } : null;

    private static object Jug(string codigo) => new { codigo, nombre = NombresDeJugadores[codigo] };

    private static object Cuerpo(
        string local, string visitante, int jornada, string? hora, string estado, (int, int)? marcador, (int, int)? oficial, string? motivo,
        object[]? goles = null, object[]? autogoles = null, (int, int)? golesSinAutor = null, bool? tarjetasInformadas = null, object[]? tarjetas = null)
    {
        string Nombre(string c) => Equipos3.Single(e => e.Codigo == c).Nombre;
        return new
        {
            torneo = new { codigo = Torneo3, nombre = "Torneo Tres" },
            fase = new { orden = 1 },
            jornada,
            programadoPara = hora,
            local = Eq(local, Nombre(local)),
            visitante = Eq(visitante, Nombre(visitante)),
            estado,
            marcador = Marc(marcador),
            oficial = Marc(oficial),
            motivo,
            goles = goles ?? [],
            autogoles = autogoles ?? [],
            golesSinAutor = Marc(golesSinAutor),
            tarjetasInformadas,
            tarjetas = tarjetas ?? [],
            resumen = (object?)null,
        };
    }

    private static object Gol(string equipo, string jugador, int cantidad) => new { equipo, jugador = Jug(jugador), cantidad };

    private static object Autogol(string delAutor, string beneficiado, string jugador, int cantidad) =>
        new { equipoDelAutor = delAutor, equipoBeneficiado = beneficiado, jugador = Jug(jugador), cantidad };

    private static object Tarjeta(string equipo, string jugador, string color, int cantidad) => new { equipo, jugador = Jug(jugador), color, cantidad };

    /// <summary>Los seis cuerpos de T-03, por clave natural (local, visitante).</summary>
    private static Dictionary<(string, string), object> Esperados() => new()
    {
        [("E1", "E2")] = Cuerpo("E1", "E2", 1, null, "Jugado", (6, 3), null, null,
            goles: [Gol("local", "P11", 2), Gol("local", "P12", 1), Gol("local", "P13", 1), Gol("visitante", "P21", 2)],
            autogoles: [Autogol("local", "visitante", "P14", 1), Autogol("visitante", "local", "P22", 1)],
            golesSinAutor: (1, 0), tarjetasInformadas: true,
            tarjetas:
            [
                Tarjeta("local", "P11", "Amarilla", 1), Tarjeta("local", "P11", "Azul", 1), Tarjeta("local", "P12", "Azul", 1),
                Tarjeta("visitante", "P21", "Amarilla", 2), Tarjeta("visitante", "P22", "Roja", 1),
            ]),
        [("E3", "E4")] = Cuerpo("E3", "E4", 1, null, "Jugado", (1, 0), null, null, golesSinAutor: (1, 0), tarjetasInformadas: false),
        [("E5", "E6")] = Cuerpo("E5", "E6", 1, null, "Adjudicado", (2, 1), (3, 0), "Sancion", golesSinAutor: (2, 1), tarjetasInformadas: true),
        [("E7", "E8")] = Cuerpo("E7", "E8", 1, null, "Adjudicado", null, (3, 0), "WalkOver"),
        [("E1", "E3")] = Cuerpo("E1", "E3", 2, null, "Suspendido", (1, 0), null, "Clima",
            goles: [Gol("local", "P11", 1)], golesSinAutor: (0, 0), tarjetasInformadas: true),
        [("E2", "E4")] = Cuerpo("E2", "E4", 2, "2026-11-21T20:00:00Z", "Programado", null, null, null),
    };

    [Fact] // T-03
    public async Task T03_cada_detalle_devuelve_el_cuerpo_exacto_de_su_estado_y_se_cumple_I05()
    {
        await using var e = new EntornoDeLecturasPublicas(db);
        var liga = await e.CrearLiga();
        await CargarT3(e, liga);

        foreach (var ((local, visitante), esperado) in Esperados())
        {
            var texto = await e.Texto(Detalle(liga, Torneo3, 1, local, visitante));
            IgualesLiteralmente(esperado, texto);
            ExigirI05(JsonNode.Parse(texto)!);
        }
    }

    [Fact] // T-03: Cancelado
    public async Task T03_un_partido_cancelado_se_publica_sin_marcador_ni_hechos_de_planilla()
    {
        await using var e = new EntornoDeLecturasPublicas(db);
        var liga = await e.CrearLiga();
        await CargarT3(e, liga);

        IgualesLiteralmente(Cuerpo("E5", "E7", 2, null, "Cancelado", null, null, null), await e.Texto(Detalle(liga, Torneo3, 1, "E5", "E7")));
    }

    /// <summary>I-05: por cada lado, los goles de sus jugadores + los autogoles que se le acreditan + sus goles sin autor = su marcador.</summary>
    private static void ExigirI05(JsonNode cuerpo)
    {
        if (cuerpo["marcador"] is not JsonObject marcador) return;

        foreach (var lado in new[] { "local", "visitante" })
        {
            var otro = lado == "local" ? "visitante" : "local";
            var deJugadores = cuerpo["goles"]!.AsArray().Where(g => (string)g!["equipo"]! == lado).Sum(g => (int)g!["cantidad"]!);
            var autogoles = cuerpo["autogoles"]!.AsArray().Where(g => (string)g!["equipoBeneficiado"]! == lado).Sum(g => (int)g!["cantidad"]!);
            var sinAutor = (int)cuerpo["golesSinAutor"]![lado]!;
            Assert.Equal((int)marcador[lado]!, deJugadores + autogoles + sinAutor);
            Assert.All(cuerpo["autogoles"]!.AsArray().Where(g => (string)g!["equipoBeneficiado"]! == lado),
                g => Assert.Equal(otro, (string)g!["equipoDelAutor"]!));
        }
    }

    [Fact] // T-16 (I-04): el detalle y la lista leen la misma fuente de estado, marcador, oficial y motivo
    public async Task T16_para_cada_partido_de_T02_el_detalle_coincide_con_su_fila_de_la_lista()
    {
        await using var e = new EntornoDeLecturasPublicas(db);
        var liga = await e.CrearLiga();
        await e.CargarT02(liga);

        var lista = (await e.Cuerpo(Partidos(liga, TorneoT02)))["partidos"]!.AsArray();
        Assert.Equal(8, lista.Count);
        foreach (var fila in lista)
        {
            var local = (string)fila!["local"]!["codigo"]!;
            var visitante = (string)fila["visitante"]!["codigo"]!;
            var detalle = await e.Cuerpo(Detalle(liga, TorneoT02, 1, local, visitante));

            foreach (var miembro in new[] { "estado", "marcador", "oficial", "motivo", "jornada", "programadoPara" })
                Assert.Equal(fila[miembro]?.ToJsonString(), detalle[miembro]?.ToJsonString());
        }
    }

    // ---- T-13 (detalle) ----

    [Theory] // T-13 (a): cualquier valor mal formado de la ruta, en un slug existente: 400, cero comandos, el resolutor sin llamadas
    [MemberData(nameof(RutasMalFormadas))]
    public async Task T13_un_valor_mal_formado_da_400_sin_comandos_de_base_ni_resolucion_del_slug(string ruta)
    {
        await using var e = new EntornoDeLecturasPublicas(db);

        var (estado, _, comandos, resoluciones) = await e.Medir($"/liga-a{ruta}");

        Assert.Equal(HttpStatusCode.BadRequest, estado);
        Assert.Equal((0, 0), (comandos, resoluciones));
    }

    public static IEnumerable<object[]> RutasMalFormadas()
    {
        foreach (var orden in OrdenesMalFormados) yield return [$"/torneos/T3/fases/{orden}/partidos/E1/E2"];
        foreach (var codigo in CodigosMalFormados)
        {
            yield return [$"/torneos/{codigo}/fases/1/partidos/E1/E2"];
            yield return [$"/torneos/T3/fases/1/partidos/{codigo}/E2"];
            yield return [$"/torneos/T3/fases/1/partidos/E1/{codigo}"];
        }
    }

    [Fact] // T-13 (b): lo inexistente, la clave invertida de una vuelta y los códigos que solo difieren en mayúsculas dan 404
    public async Task T13_lo_inexistente_la_clave_invertida_y_las_mayusculas_dan_404_con_el_cuerpo_de_cualquier_404()
    {
        await using var e = new EntornoDeLecturasPublicas(db);
        var liga = await e.CrearLiga();
        await CargarT3(e, liga);
        var (_, deRutaDesconocida) = await EntornoDeEstadisticas.Leer(await e.Get($"/{liga}/ruta-que-no-existe/x"));
        Assert.Equal(HttpStatusCode.OK, (await e.Get(Detalle(liga, Torneo3, 1, "E1", "E2"))).StatusCode); // control positivo

        foreach (var ruta in new[]
        {
            Detalle("liga-que-no-existe", Torneo3, 1, "E1", "E2"),
            Detalle(liga, "NO-EXISTE", 1, "E1", "E2"),
            Detalle(liga, Torneo3, 7, "E1", "E2"),
            Detalle(liga, Torneo3, 1, "E1", "E8"),   // partido inexistente
            Detalle(liga, Torneo3, 1, "E2", "E1"),   // clave invertida de un partido de una vuelta: no es un alias
            Detalle(liga, "t3", 1, "E1", "E2"),      // solo difiere en mayúsculas
            Detalle(liga, Torneo3, 1, "e1", "E2"),
            Detalle(liga, Torneo3, 1, "E1", "e2"),
        })
        {
            var (estado, texto) = await EntornoDeEstadisticas.Leer(await e.Get(ruta));
            Assert.True(estado == HttpStatusCode.NotFound, $"{ruta} debía dar 404 y dio {estado}");
            Assert.Equal(deRutaDesconocida, texto);
        }
    }

    [Fact] // T-13 (c): orden=01 responde exactamente como 1 (C-25)
    public async Task T13_el_orden_01_responde_igual_que_el_1()
    {
        await using var e = new EntornoDeLecturasPublicas(db);
        var liga = await e.CrearLiga();
        await CargarT3(e, liga);

        var con1 = await e.Texto(Detalle(liga, Torneo3, 1, "E1", "E2"));
        var con01 = await e.Texto($"/{liga}/torneos/{Torneo3}/fases/01/partidos/E1/E2");

        Assert.Equal(con1, con01);
    }

    [Fact] // T-13 (d) y R-23: un Authorization inválido no cambia el cuerpo ni el número de comandos
    public async Task T13_un_Authorization_invalido_da_el_mismo_cuerpo_y_los_mismos_comandos_que_sin_el()
    {
        await using var e = new EntornoDeLecturasPublicas(db);
        var liga = await e.CrearLiga();
        await CargarT3(e, liga);
        var ruta = Detalle(liga, Torneo3, 1, "E1", "E2");

        var sin = await e.Medir(ruta);
        var con = await e.Medir(ruta, "Bearer x");

        Assert.Equal(HttpStatusCode.OK, sin.Estado);
        Assert.Equal((sin.Estado, sin.Texto, sin.Comandos), (con.Estado, con.Texto, con.Comandos));
    }

    // ---- T-12 y T-15 (detalle) ----

    [Fact] // T-12 (detalle)
    public async Task T12_el_detalle_no_filtra_ningun_identificador_ni_miembro_interno()
    {
        await using var e = new EntornoDeLecturasPublicas(db);
        var liga = await e.CrearLiga();
        await CargarT3(e, liga);

        foreach (var (local, visitante) in Esperados().Keys)
            SinFugas(await e.Texto(Detalle(liga, Torneo3, 1, local, visitante)));
    }

    [Fact] // T-15 (detalle): el fixture de T-03 insertado en orden inverso da cuerpos idénticos, byte a byte
    public async Task T15_el_detalle_no_depende_del_orden_de_insercion()
    {
        await using var e = new EntornoDeLecturasPublicas(db);
        var (directa, inversa) = (await e.CrearLiga(), await e.CrearLiga());
        await CargarT3(e, directa);
        await CargarT3(e, inversa, inverso: true);

        foreach (var (local, visitante) in Esperados().Keys)
            Assert.Equal(
                await e.Texto(Detalle(directa, Torneo3, 1, local, visitante)),
                await e.Texto(Detalle(inversa, Torneo3, 1, local, visitante)));
    }
}
