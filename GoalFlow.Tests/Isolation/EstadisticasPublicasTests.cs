using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using static GoalFlow.Tests.Isolation.BaseDeAislamiento;
using static GoalFlow.Tests.Isolation.EntornoDeEstadisticas;

namespace GoalFlow.Tests.Isolation;

/// <summary>
/// Spec 009 commit 6: los tres endpoints públicos de estadísticas por la API real contra PostgreSQL (T-11 a T-15, T-17, T-19
/// integración, T-21 y T-22). Los JSON esperados se arman aquí desde el Apéndice A. Colección de la planilla: sin paralelismo.
/// </summary>
[Collection(ColeccionDePlanilla.Nombre)]
public partial class EstadisticasPublicasTests(BaseDeAislamiento db)
{
    [GeneratedRegex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex PatronDeGuid();

    private static JsonNode Nodo(object valor) => JsonSerializer.SerializeToNode(valor, Json)!;

    private static async Task<JsonNode> Cuerpo(HttpResponseMessage respuesta, HttpStatusCode esperado = HttpStatusCode.OK)
    {
        var (estado, texto) = await Leer(respuesta);
        Assert.True(estado == esperado, $"Se esperaba {esperado} y llegó {estado}: {texto}");
        return JsonNode.Parse(texto)!;
    }

    private static void SinFugas(string texto, string[]? ademas = null)
    {
        Assert.DoesNotMatch(PatronDeGuid(), texto);
        foreach (var prohibido in new[] { UsuarioU1.ToString(), UsuarioU2.ToString(), "fk_", "ux_", "ak_", "23503", "23505", "constraint", "xmin", "Npgsql", "guardadoPor" }.Concat(ademas ?? []))
            Assert.DoesNotContain(prohibido, texto, StringComparison.OrdinalIgnoreCase);
    }

    // ---- Esperados de F6, armados desde el Apéndice A ----

    private static object Equipo6(string p, int i) => new { codigo = Equipo(p, i), nombre = $"Equipo E{i}" };

    private static object FilaF6(string p, int pos, int e, int pj, int g, int emp, int per, int gf, int gc, int dg, int ppr, int pts, int tc, int ts) => new
    {
        posicion = pos, equipo = Equipo6(p, e), pj, pg = g, pe = emp, pp = per, gf, gc, dg,
        puntosPorResultado = ppr, deducciones = 0, puntos = pts, partidosConTarjetasComputables = tc, partidosConTarjetasSinInformar = ts,
    };

    private static JsonNode PosicionesF6(string p, string estado = "Provisional", int version = 1) => Nodo(new
    {
        torneo = new { codigo = p, nombre = "Torneo F6" },
        reglamento = new { version, estado },
        fase = new { orden = 1 },
        filas = new[]
        {
            FilaF6(p, 1, 1, 5, 3, 2, 0, 7, 3, 4, 11, 11, 5, 2),
            FilaF6(p, 2, 2, 5, 1, 2, 2, 5, 7, -2, 5, 5, 5, 0),
            FilaF6(p, 3, 3, 3, 1, 1, 1, 5, 3, 2, 4, 4, 2, 0),
            FilaF6(p, 4, 5, 2, 1, 0, 1, 3, 3, 0, 3, 3, 2, 1),
            FilaF6(p, 5, 6, 2, 0, 2, 0, 2, 2, 0, 2, 2, 2, 0),
            FilaF6(p, 6, 4, 3, 0, 1, 2, 0, 4, -4, 1, 1, 2, 1),
        },
        empates = Array.Empty<object>(),
        cobertura = new { partidosConTarjetasComputables = 9, partidosConTarjetasSinInformar = 2 },
    });

    private static object Goleador(string p, int pos, string numero, int equipo, int goles) => new
    {
        posicion = pos, jugador = new { codigo = Jugador(p, numero), nombre = $"Jugador P{numero}" }, equipo = Equipo6(p, equipo), goles,
    };

    private static object PorEquipo(string p, int e, int partidos, int conSinAutor, int sinAutor) => new
    {
        equipo = Equipo6(p, e), partidosComputables = partidos, partidosConGolesSinAutor = conSinAutor, golesSinAutor = sinAutor,
    };

    private static JsonNode GoleadoresF6(string p, string estado = "Provisional", int version = 1) => Nodo(new
    {
        torneo = new { codigo = p, nombre = "Torneo F6" },
        reglamento = new { version, estado },
        filas = new[]
        {
            Goleador(p, 1, "21", 2, 4), Goleador(p, 2, "51", 5, 3), Goleador(p, 3, "11", 1, 2), Goleador(p, 3, "12", 1, 2),
            Goleador(p, 5, "22", 2, 1), Goleador(p, 5, "61", 6, 1), Goleador(p, 5, "62", 6, 1),
        },
        cobertura = new
        {
            partidosComputables = 9, partidosConGolesSinAutor = 3, golesTotales = 19, golesDeJugador = 14, autogoles = 1,
            golesSinAutor = 4, golesConAutor = 15,
            porEquipo = new[]
            {
                PorEquipo(p, 1, 5, 2, 2), PorEquipo(p, 2, 5, 0, 0), PorEquipo(p, 3, 2, 1, 2),
                PorEquipo(p, 4, 2, 0, 0), PorEquipo(p, 5, 2, 0, 0), PorEquipo(p, 6, 2, 0, 0),
            },
        },
    });

    private static object FilaDeValla(string p, int pos, int e, int partidos, int goles) => new
    {
        posicion = pos, equipo = Equipo6(p, e), partidosComputados = partidos, golesEnContra = goles,
    };

    private static JsonNode VallaF6(string p, string estado = "Provisional", int version = 1) => Nodo(new
    {
        torneo = new { codigo = p, nombre = "Torneo F6" },
        reglamento = new { version, estado },
        filas = new[]
        {
            FilaDeValla(p, 1, 6, 2, 2), FilaDeValla(p, 2, 1, 5, 3), FilaDeValla(p, 2, 3, 3, 3),
            FilaDeValla(p, 2, 5, 2, 3), FilaDeValla(p, 5, 4, 3, 4), FilaDeValla(p, 6, 2, 5, 7),
        },
    });

    // ---- T-11 ----

    [Fact]
    public async Task T11_F6_cargado_por_la_API_real_devuelve_los_tres_cuerpos_del_Apendice_con_el_reglamento_Provisional()
    {
        await using var e = new EntornoDeEstadisticas(db);
        var p = Prefijo();
        await e.CargarF6("liga-a", p, ReglasDeF6());

        Assert.True(JsonNode.DeepEquals(PosicionesF6(p), await Cuerpo(await e.Get(Posiciones("liga-a", p)))));
        Assert.True(JsonNode.DeepEquals(GoleadoresF6(p), await Cuerpo(await e.Get(Goleadores("liga-a", p)))));
        Assert.True(JsonNode.DeepEquals(VallaF6(p), await Cuerpo(await e.Get(Valla("liga-a", p)))));
    }

    // ---- T-12 ----

    [Fact] // T-12: un partido guardado 2–0 (versión 1) y corregido a 1–1 (versión 2) cuenta una vez, como 1–1
    public async Task T12_una_correccion_cuenta_una_sola_vez_como_1_1_en_las_posiciones_y_en_los_goleadores()
    {
        await using var e = new EntornoDeEstadisticas(db);
        var p = Prefijo();
        var cuerpo = Nodo(EntornoDePlanilla.Torneo(p)).AsObject();
        cuerpo["reglamento"] = ReglasDeF6();
        await e.ImportarConExito("liga-a", p, cuerpo);
        var ruta = EntornoDePlanilla.RutaDelPrimerPartido(p);
        var (j11, j21) = (EntornoDePlanilla.Jugador(p, 1, 1), EntornoDePlanilla.Jugador(p, 2, 1));
        Assert.Equal(HttpStatusCode.OK, (await e.Base.Guardar(
            ruta, new PlanillaJ(0, "Jugado", new(2, 0), TarjetasInformadas: true, Goles: [new(j11, 2)]))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await e.Base.Guardar(
            ruta, new PlanillaJ(1, "Jugado", new(1, 1), TarjetasInformadas: true, Goles: [new(j11, 1), new(j21, 1)]))).StatusCode);

        var posiciones = await Cuerpo(await e.Get(Posiciones("liga-a", p)));
        var goleadores = await Cuerpo(await e.Get(Goleadores("liga-a", p)));

        var filas = posiciones["filas"]!.AsArray().Where(f => (int)f!["pj"]! > 0).ToList();
        Assert.Equal(2, filas.Count);
        Assert.All(filas, f =>
        {
            Assert.Equal(1, (int)f!["pj"]!);
            Assert.Equal(1, (int)f["pe"]!);
            Assert.Equal(1, (int)f["puntos"]!);
        });
        Assert.Equal([(j11, 1), (j21, 1)],
            goleadores["filas"]!.AsArray().Select(f => ((string)f!["jugador"]!["codigo"]!, (int)f["goles"]!)).Order().ToArray());
    }

    // ---- T-13 ----

    [Fact]
    public async Task T13_sin_reglamento_y_con_reglamentos_incompletos_la_API_pide_la_configuracion_que_falta_y_nunca_la_asume()
    {
        await using var e = new EntornoDeEstadisticas(db);
        var p = Prefijo();
        await e.CargarF6("liga-a", p, reglamento: null);

        // Sin reglamento: posiciones [puntos, desempate, adjudicacion.WalkOver]; valla [adjudicacion.WalkOver]; goleadores 200 con reglamento nulo.
        await FaltantesSon(e, Posiciones("liga-a", p), "puntos", "desempate", "adjudicacion.WalkOver");
        await FaltantesSon(e, Valla("liga-a", p), "adjudicacion.WalkOver");
        var sinReglas = await Cuerpo(await e.Get(Goleadores("liga-a", p)));
        Assert.Null(sinReglas["reglamento"]);

        // Solo puntos y desempate: queda adjudicacion.WalkOver en posiciones y valla; goleadores 200 (M10 no tiene marcador deportivo).
        await e.ImportarConExito("liga-a", p, CuerpoDeF6(p, ReglasDeF6(adjudicacion: false)));
        await FaltantesSon(e, Posiciones("liga-a", p), "adjudicacion.WalkOver");
        await FaltantesSon(e, Valla("liga-a", p), "adjudicacion.WalkOver");
        Assert.Equal(1, (int)(await Cuerpo(await e.Get(Goleadores("liga-a", p))))["reglamento"]!["version"]!);

        // Sin desempate: queda desempate.
        await e.ImportarConExito("liga-a", p, CuerpoDeF6(p, ReglasDeF6(desempate: false)));
        await FaltantesSon(e, Posiciones("liga-a", p), "desempate");

        // Con {"criterios": []} el desempate está decidido (ninguno): 200.
        await e.ImportarConExito("liga-a", p, CuerpoDeF6(p, ReglasDeF6()));
        Assert.Equal(HttpStatusCode.OK, (await e.Get(Posiciones("liga-a", p))).StatusCode);
    }

    private static async Task FaltantesSon(EntornoDeEstadisticas e, string ruta, params string[] esperados)
    {
        var cuerpo = await Cuerpo(await e.Get(ruta), HttpStatusCode.Conflict);
        Assert.Equal("configuracion_pendiente", (string)cuerpo["codigo"]!);
        Assert.Equal(esperados, cuerpo["faltantes"]!.AsArray().Select(f => (string)f!).ToArray());
        Assert.Null(cuerpo["filas"]); // y ninguna tabla
    }

    // ---- T-14 (resto): aislamiento entre ligas ----

    [Fact]
    public async Task T14_dos_ligas_con_el_mismo_torneo_y_los_mismos_equipos_ven_solo_sus_tablas_y_un_torneo_ajeno_es_404()
    {
        await using var e = new EntornoDeEstadisticas(db);
        var (p, solo) = (Prefijo(), $"SOLO-{Prefijo()}");
        JsonObject Duelo(string ganador) => new()
        {
            ["nombre"] = "Torneo Duelo",
            ["fases"] = new JsonArray(new JsonObject { ["orden"] = 1, ["tipo"] = "TodosContraTodos", ["vueltas"] = 1 }),
            ["equipos"] = new JsonArray(
                new JsonObject { ["codigo"] = Equipo(p, 1), ["nombre"] = $"Equipo {ganador} uno", ["retirado"] = false, ["jugadores"] = new JsonArray() },
                new JsonObject { ["codigo"] = Equipo(p, 2), ["nombre"] = $"Equipo {ganador} dos", ["retirado"] = false, ["jugadores"] = new JsonArray() }),
            ["partidos"] = new JsonArray(new JsonObject
            {
                ["fase"] = 1, ["jornada"] = 1, ["local"] = Equipo(p, 1), ["visitante"] = Equipo(p, 2), ["cancelado"] = false,
            }),
            ["reglamento"] = ReglasDeF6(),
        };
        await e.ImportarConExito("liga-a", p, Duelo("A"));
        await e.ImportarConExito("liga-b", p, Duelo("B"));
        await e.GuardarConExito("liga-a", p, Equipo(p, 1), Equipo(p, 2), new PlanillaJ(0, "Jugado", new(1, 0), TarjetasInformadas: true, GolesSinAutor: new(1, 0)));
        await e.GuardarConExito("liga-b", p, Equipo(p, 1), Equipo(p, 2), new PlanillaJ(0, "Jugado", new(0, 3), TarjetasInformadas: true, GolesSinAutor: new(0, 3)));
        await e.ImportarConExito("liga-a", solo, Duelo("A"));

        var deA = await Cuerpo(await e.Get(Posiciones("liga-a", p)));
        var deB = await Cuerpo(await e.Get(Posiciones("liga-b", p)));

        Assert.Equal(["Equipo A uno", "Equipo A dos"], deA["filas"]!.AsArray().Select(f => (string)f!["equipo"]!["nombre"]!).ToArray());
        Assert.Equal(["Equipo B dos", "Equipo B uno"], deB["filas"]!.AsArray().Select(f => (string)f!["equipo"]!["nombre"]!).ToArray());
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.NotFound),
            ((await e.Get(Valla("liga-a", solo))).StatusCode, (await e.Get(Valla("liga-b", solo))).StatusCode));
    }

    // ---- T-15 ----

    [Fact]
    public async Task T15_ningun_cuerpo_filtra_el_sub_un_Guid_ni_nombres_internos_y_las_rutas_mal_formadas_dan_400_sin_tocar_la_base()
    {
        await using var e = new EntornoDeEstadisticas(db);
        var p = Prefijo();
        await e.CargarF6("liga-a", p, ReglasDeF6());

        foreach (var ruta in new[] { Posiciones("liga-a", p), Goleadores("liga-a", p), Valla("liga-a", p) })
            SinFugas((await Leer(await e.Get(ruta))).Texto);
        SinFugas(await (await e.Base.Importar(p, EntornoDePlanilla.Torneo(p))).Content.ReadAsStringAsync());

        // Mal formadas: 400, y ningún comando de base.
        e.Comandos.Reiniciar();
        foreach (var ruta in new[]
        {
            Posiciones("liga-a", p, 0), Posiciones("liga-a", p, -1), $"/liga-a/torneos/{p}/fases/uno/posiciones",
            $"/liga-a/torneos/{p}/fases/99999999999/posiciones", $"/liga-a/torneos/mal%20codigo/valla",
            $"/liga-a/torneos/{new string('x', 40)}/goleadores", $"/liga-a/torneos/%C3%B1/posiciones".Replace("posiciones", "valla"),
        })
            Assert.Equal(HttpStatusCode.BadRequest, (await e.Get(ruta)).StatusCode);
        Assert.Equal(0, e.Comandos.Ejecutados);

        // Inexistentes: 404 con el mismo cuerpo vacío de la ruta desconocida.
        foreach (var ruta in new[]
        {
            Posiciones("no-existe", p), Posiciones("liga-a", "NO-EXISTE"), Posiciones("liga-a", p, 7), Goleadores("liga-a", "NO-EXISTE"), Valla("liga-a", "NO-EXISTE"),
        })
        {
            var (estado, texto) = await Leer(await e.Get(ruta));
            Assert.Equal(HttpStatusCode.NotFound, estado);
            SinFugas(texto);
        }
    }

    // ---- T-17 ----

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

    private async Task<int[]> ComandosPorEndpoint(EntornoDeEstadisticas e, string p)
    {
        var conteos = new List<int>();
        foreach (var ruta in new[] { Posiciones("liga-a", p), Goleadores("liga-a", p), Valla("liga-a", p) })
        {
            e.Comandos.Reiniciar();
            Assert.Equal(HttpStatusCode.OK, (await e.Get(ruta)).StatusCode);
            conteos.Add(e.Comandos.Ejecutados);
        }

        return [.. conteos];
    }

    [Fact]
    public async Task T17_el_numero_de_comandos_de_cada_endpoint_es_el_mismo_para_F6_y_para_un_torneo_de_20_equipos_y_190_partidos()
    {
        // Spec 010 C-20: esta prueba guarda 190 planillas para contar comandos, no prueba el limitador.
        await using var e = new EntornoDeEstadisticas(db, ajustar: b => b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["LimiteDeFrecuencia:Admin:PorCliente"] = "1000",
            ["LimiteDeFrecuencia:Admin:Global"] = "1000",
        })));
        var (f6, grande) = (Prefijo(), Prefijo());
        await e.CargarF6("liga-a", f6, ReglasDeF6());

        var partidos = VueltaCompleta(20);
        Assert.Equal(190, partidos.Count);
        var cuerpo = new JsonObject
        {
            ["nombre"] = "Torneo Grande",
            ["fases"] = new JsonArray(new JsonObject { ["orden"] = 1, ["tipo"] = "TodosContraTodos", ["vueltas"] = 1 }),
            ["equipos"] = new JsonArray([.. Enumerable.Range(1, 20).Select(i => (JsonNode)new JsonObject
            {
                ["codigo"] = Equipo(grande, i), ["nombre"] = $"Equipo G{i}", ["retirado"] = false, ["jugadores"] = new JsonArray(),
            })]),
            ["partidos"] = new JsonArray([.. partidos.Select(m => (JsonNode)new JsonObject
            {
                ["fase"] = 1, ["jornada"] = m.Jornada, ["local"] = Equipo(grande, m.Local), ["visitante"] = Equipo(grande, m.Visitante), ["cancelado"] = false,
            })]),
            ["reglamento"] = ReglasDeF6(),
        };
        await e.ImportarConExito("liga-a", grande, cuerpo);
        foreach (var m in partidos)
            await e.GuardarConExito("liga-a", grande, Equipo(grande, m.Local), Equipo(grande, m.Visitante),
                new PlanillaJ(0, "Jugado", new(1, 0), TarjetasInformadas: true, GolesSinAutor: new(1, 0)));

        var conteosF6 = await ComandosPorEndpoint(e, f6);
        var conteosGrande = await ComandosPorEndpoint(e, grande);

        Console.WriteLine($"T17 comandos [posiciones, goleadores, valla] F6: {string.Join(", ", conteosF6)}; 20 equipos y 190 partidos: {string.Join(", ", conteosGrande)}");
        Assert.Equal(conteosF6, conteosGrande);
        Assert.All(conteosF6, n => Assert.InRange(n, 1, 20));
    }

    // ---- T-19 (integración) ----

    [Fact]
    public async Task T19_F6_cargado_en_orden_inverso_da_cuerpos_identicos_byte_a_byte()
    {
        await using var e = new EntornoDeEstadisticas(db);
        var p = Prefijo();
        await e.CargarF6("liga-a", p, ReglasDeF6());
        await e.CargarF6("liga-b", p, ReglasDeF6(), inverso: true);

        foreach (var ruta in new Func<string, string>[] { l => Posiciones(l, p), l => Goleadores(l, p), l => Valla(l, p) })
        {
            var (estadoA, a) = await Leer(await e.Get(ruta("liga-a")));
            var (estadoB, b) = await Leer(await e.Get(ruta("liga-b")));
            Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (estadoA, estadoB));
            Assert.Equal(a, b);
        }
    }

    // ---- T-21 ----

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    public async Task T21_con_el_interruptor_ausente_o_false_las_tres_rutas_dan_404_como_una_ruta_desconocida_y_sin_comandos_de_base(string? valor)
    {
        var p = Prefijo();
        await using (var preparacion = new EntornoDeEstadisticas(db))
            await preparacion.CargarF6("liga-a", p, ReglasDeF6());

        await using var e = new EntornoDeEstadisticas(db, interruptor: valor);
        var (_, cuerpoDeRutaDesconocida) = await Leer(await e.Get($"/liga-a/torneos/{p}/no-es-una-ruta"));
        e.Comandos.Reiniciar();

        foreach (var ruta in new[] { Posiciones("liga-a", p), Goleadores("liga-a", p), Valla("liga-a", p) })
        {
            var (estado, texto) = await Leer(await e.Get(ruta));
            Assert.Equal(HttpStatusCode.NotFound, estado);
            Assert.Equal(cuerpoDeRutaDesconocida, texto);
        }

        Assert.Equal(0, e.Comandos.Ejecutados);
    }

    [Fact]
    public async Task T21_con_el_interruptor_en_true_responde_200()
    {
        await using var e = new EntornoDeEstadisticas(db, interruptor: "true");
        var p = Prefijo();
        await e.CargarF6("liga-a", p, ReglasDeF6());

        foreach (var ruta in new[] { Posiciones("liga-a", p), Goleadores("liga-a", p), Valla("liga-a", p) })
            Assert.Equal(HttpStatusCode.OK, (await e.Get(ruta)).StatusCode);
    }

    [Fact]
    public void T21_los_archivos_de_configuracion_versionados_dejan_el_interruptor_ausente_o_false()
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
                && doc.RootElement.TryGetProperty("Estadisticas", out var seccion)
                && seccion.TryGetProperty("EndpointsPublicosHabilitados", out var valor))
                Assert.True(valor.ValueKind == JsonValueKind.False, $"{Path.GetFileName(archivo)} debe dejar el interruptor en false");
        }

        // El predeterminado es false: appsettings.json lo declara explícitamente.
        using var principal = JsonDocument.Parse(File.ReadAllText(archivos.First(f => f.EndsWith("appsettings.json", StringComparison.Ordinal))));
        Assert.Equal(JsonValueKind.False, principal.RootElement.GetProperty("Estadisticas").GetProperty("EndpointsPublicosHabilitados").ValueKind);
    }

    // ---- T-22 ----

    [Fact]
    public async Task T22_Confirmado_no_cambia_ningun_calculo_solo_el_reglamento_de_la_respuesta()
    {
        await using var e = new EntornoDeEstadisticas(db);
        var p = Prefijo();
        await e.CargarF6("liga-a", p, ReglasDeF6("Provisional"));
        var rutas = new[] { Posiciones("liga-a", p), Goleadores("liga-a", p), Valla("liga-a", p) };
        var antes = new List<JsonNode>();
        foreach (var ruta in rutas) antes.Add(await Cuerpo(await e.Get(ruta)));

        await e.ImportarConExito("liga-a", p, CuerpoDeF6(p, ReglasDeF6("Confirmado")));

        for (var i = 0; i < rutas.Length; i++)
        {
            var despues = await Cuerpo(await e.Get(rutas[i]));
            Assert.Equal((1, "Provisional"), ((int)antes[i]["reglamento"]!["version"]!, (string)antes[i]["reglamento"]!["estado"]!));
            Assert.Equal((2, "Confirmado"), ((int)despues["reglamento"]!["version"]!, (string)despues["reglamento"]!["estado"]!));
            antes[i].AsObject().Remove("reglamento");
            despues.AsObject().Remove("reglamento");
            Assert.True(JsonNode.DeepEquals(antes[i], despues), $"El cuerpo de {rutas[i]} cambió más que el reglamento");
        }
    }
}
