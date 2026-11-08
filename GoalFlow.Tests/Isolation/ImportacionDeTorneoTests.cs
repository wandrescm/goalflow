using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using static GoalFlow.Tests.Isolation.BaseDeAislamiento;

namespace GoalFlow.Tests.Isolation;

/// <summary>
/// Colección propia, sin paralelismo y con su propia base: T-05 mide todas las tablas del schema y ninguna otra
/// prueba debe escribir en ella mientras tanto (Spec 005 C-15).
/// </summary>
[CollectionDefinition(Nombre, DisableParallelization = true)]
public class ColeccionDeImportacion : ICollectionFixture<BaseDeAislamiento>
{
    public const string Nombre = "Importacion";
}

/// <summary>Spec 005 T-04 a T-14 y T-28 a T-31: la importación idempotente por la API real, contra PostgreSQL.</summary>
[Collection(ColeccionDeImportacion.Nombre)]
public class ImportacionDeTorneoTests(BaseDeAislamiento db)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly LlaveDePrueba Llave = new();

    // ---- Contrato de la petición (Spec 005 R-14) ----

    private sealed record JugadorJ(string Codigo, string Nombre);

    private sealed record EquipoJ(string Codigo, string Nombre, bool Retirado, List<JugadorJ> Jugadores);

    private sealed record FaseJ(int Orden, string Tipo, int Vueltas);

    private sealed record PartidoJ(int Fase, int Jornada, string Local, string Visitante, string? ProgramadoEn, bool Cancelado);

    private sealed record TorneoJ(string Nombre, List<FaseJ> Fases, List<EquipoJ> Equipos, List<PartidoJ> Partidos);

    /// <summary>Una fase, cuatro equipos con tres jugadores cada uno y seis partidos. Los códigos llevan un prefijo único por prueba.</summary>
    private static TorneoJ Basico(string p)
    {
        var equipos = Enumerable.Range(1, 4)
            .Select(e => new EquipoJ(
                $"{p}-EQ-{e:00}", $"Equipo {NombreDe(e)}", false,
                [.. Enumerable.Range(1, 3).Select(j => new JugadorJ($"{p}-J-{e}{j}", $"Jugador {NombreDe(e)}{j}"))]))
            .ToList();
        string C(int i) => $"{p}-EQ-{i:00}";
        List<PartidoJ> partidos =
        [
            new(1, 1, C(1), C(2), "2026-11-08T15:00:00-05:00", false),
            new(1, 1, C(3), C(4), null, false),
            new(1, 2, C(1), C(3), null, false),
            new(1, 2, C(2), C(4), null, false),
            new(1, 3, C(1), C(4), null, false),
            new(1, 3, C(2), C(3), null, false),
        ];
        return new TorneoJ("Torneo Uno", [new FaseJ(1, "TodosContraTodos", 1)], equipos, partidos);
    }

    private static string NombreDe(int i) => i switch { 1 => "Uno", 2 => "Dos", 3 => "Tres", _ => "Cuatro" };

    private static string Prefijo() => Guid.NewGuid().ToString("N")[..6];

    // ---- Infraestructura de la prueba ----

    private static string TokenDe(Guid usuario) => TokensDePrueba.Firmar(Llave, usuario);

    private WebApplicationFactory<Program> Fabrica(CapturaDeLogs? logs = null) =>
        AnfitrionDeAdmin.Crear(db, new FuenteDeJwksDePrueba(Llave), logs);

    private static HttpRequestMessage Peticion(string liga, string torneo, string cuerpo, Guid? usuario)
    {
        var peticion = new HttpRequestMessage(HttpMethod.Put, $"/{liga}/admin/torneos/{torneo}")
        {
            Content = new StringContent(cuerpo, Encoding.UTF8, "application/json"),
        };
        if (usuario is { } u) peticion.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TokenDe(u));
        return peticion;
    }

    private static Task<HttpResponseMessage> Importar(
        HttpClient cliente, string liga, string torneo, TorneoJ fixture, Guid? usuario = null) =>
        cliente.SendAsync(Peticion(liga, torneo, JsonSerializer.Serialize(fixture, Json), usuario ?? (liga == "liga-b" ? UsuarioU2 : UsuarioU1)));

    private static Task<HttpResponseMessage> ImportarTexto(
        HttpClient cliente, string torneo, string cuerpo) =>
        cliente.SendAsync(Peticion("liga-a", torneo, cuerpo, UsuarioU1));

    private static async Task<JsonDocument> Cuerpo(HttpResponseMessage respuesta) =>
        JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());

    private static (int Creados, int Actualizados, int SinCambios) Conteo(JsonDocument doc, string coleccion)
    {
        var c = doc.RootElement.GetProperty(coleccion);
        return (c.GetProperty("creados").GetInt32(), c.GetProperty("actualizados").GetInt32(), c.GetProperty("sinCambios").GetInt32());
    }

    private async Task<object?> Escalar(string sql)
    {
        await using var conexion = new NpgsqlConnection(db.CadenaSuperusuario);
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand(sql, conexion);
        return await comando.ExecuteScalarAsync();
    }

    private async Task<long> Contar(string sql) => Convert.ToInt64(await Escalar(sql));

    private Task<long> TorneosConCodigo(string codigo) =>
        Contar($"select count(*) from goalflow.torneos where codigo = '{codigo}'");

    /// <summary>Foto de todas las tablas del schema como superusuario: filas y multiconjunto de xmin (C-15). Es una medición, no una prueba de aislamiento.</summary>
    private async Task<Dictionary<string, string[]>> Foto()
    {
        await using var conexion = new NpgsqlConnection(db.CadenaSuperusuario);
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

        Assert.True(foto.Count >= 9, "La foto debe cubrir todas las tablas del schema goalflow");
        return foto;
    }

    private static void FotosIguales(Dictionary<string, string[]> antes, Dictionary<string, string[]> despues)
    {
        Assert.Equal(antes.Keys.Order(), despues.Keys.Order());
        foreach (var (tabla, versiones) in antes)
        {
            Assert.True(versiones.SequenceEqual(despues[tabla]), $"La tabla {tabla} cambió (filas o xmin)");
        }
    }

    private async Task PrimeraImportacion(HttpClient cliente, string torneo, TorneoJ fixture) =>
        Assert.Equal(HttpStatusCode.OK, (await Importar(cliente, "liga-a", torneo, fixture)).StatusCode);

    // ---- Pruebas ----

    [Fact] // T-04
    public async Task T04_la_primera_importacion_responde_200_con_los_conteos_creados_y_guarda_la_hora_en_utc()
    {
        await using var fabrica = Fabrica();
        var cliente = fabrica.CreateClient();
        var (p, codigo) = (Prefijo(), $"T04-{Prefijo()}");

        var respuesta = await Importar(cliente, "liga-a", codigo, Basico(p));

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        using var doc = await Cuerpo(respuesta);
        Assert.Equal((1, 0, 0), Conteo(doc, "torneo"));
        Assert.Equal((1, 0, 0), Conteo(doc, "fases"));
        Assert.Equal((4, 0, 0), Conteo(doc, "equipos"));
        Assert.Equal((12, 0, 0), Conteo(doc, "jugadores"));
        Assert.Equal((4, 0, 0), Conteo(doc, "participaciones"));
        Assert.Equal((12, 0, 0), Conteo(doc, "inscripciones"));
        Assert.Equal((6, 0, 0), Conteo(doc, "partidos"));

        var hora = await Escalar($"""
            select to_char(p.programado_en at time zone 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"')
            from goalflow.partidos p join goalflow.torneos t on t.id = p.torneo_id
            where t.codigo = '{codigo}' and p.programado_en is not null
            """);
        Assert.Equal("2026-11-08T20:00:00Z", hora);
    }

    [Fact] // T-05
    public async Task T05_repetir_la_importacion_no_cambia_nada_ni_escribe_ninguna_fila_en_el_schema()
    {
        await using var fabrica = Fabrica();
        var cliente = fabrica.CreateClient();
        var (fixture, codigo) = (Basico(Prefijo()), $"T05-{Prefijo()}");
        await PrimeraImportacion(cliente, codigo, fixture);
        var antes = await Foto();

        var segunda = await Importar(cliente, "liga-a", codigo, fixture);

        Assert.Equal(HttpStatusCode.OK, segunda.StatusCode);
        using var doc = await Cuerpo(segunda);
        Assert.Equal((0, 0, 1), Conteo(doc, "torneo"));
        Assert.Equal((0, 0, 1), Conteo(doc, "fases"));
        Assert.Equal((0, 0, 4), Conteo(doc, "equipos"));
        Assert.Equal((0, 0, 12), Conteo(doc, "jugadores"));
        Assert.Equal((0, 0, 4), Conteo(doc, "participaciones"));
        Assert.Equal((0, 0, 12), Conteo(doc, "inscripciones"));
        Assert.Equal((0, 0, 6), Conteo(doc, "partidos"));
        FotosIguales(antes, await Foto());
    }

    [Fact] // T-06
    public async Task T06_cambiar_un_equipo_un_jugador_y_una_hora_actualiza_exactamente_esos_tres()
    {
        await using var fabrica = Fabrica();
        var cliente = fabrica.CreateClient();
        var (fixture, codigo) = (Basico(Prefijo()), $"T06-{Prefijo()}");
        await PrimeraImportacion(cliente, codigo, fixture);

        var cambiado = fixture with
        {
            Equipos =
            [
                fixture.Equipos[0] with
                {
                    Nombre = "Equipo Uno Renombrado",
                    Jugadores = [fixture.Equipos[0].Jugadores[0] with { Nombre = "Jugador Renombrado" }, .. fixture.Equipos[0].Jugadores.Skip(1)],
                },
                .. fixture.Equipos.Skip(1),
            ],
            Partidos = [fixture.Partidos[0] with { ProgramadoEn = "2026-11-09T16:30:00Z" }, .. fixture.Partidos.Skip(1)],
        };
        var respuesta = await Importar(cliente, "liga-a", codigo, cambiado);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        using var doc = await Cuerpo(respuesta);
        Assert.Equal((0, 0, 1), Conteo(doc, "torneo"));
        Assert.Equal((0, 0, 1), Conteo(doc, "fases"));
        Assert.Equal((0, 1, 3), Conteo(doc, "equipos"));
        Assert.Equal((0, 1, 11), Conteo(doc, "jugadores"));
        Assert.Equal((0, 0, 4), Conteo(doc, "participaciones"));
        Assert.Equal((0, 0, 12), Conteo(doc, "inscripciones"));
        Assert.Equal((0, 1, 5), Conteo(doc, "partidos"));
    }

    [Fact] // T-07
    public async Task T07_omitir_un_partido_y_un_jugador_responde_409_con_ambas_claves_y_no_cambia_la_base()
    {
        await using var fabrica = Fabrica();
        var cliente = fabrica.CreateClient();
        var (p, codigo) = (Prefijo(), $"T07-{Prefijo()}");
        var fixture = Basico(p);
        await PrimeraImportacion(cliente, codigo, fixture);
        var antes = await Foto();
        var sinJugador = fixture.Equipos[1].Jugadores[0].Codigo;
        var recortado = fixture with
        {
            Equipos = [fixture.Equipos[0], fixture.Equipos[1] with { Jugadores = [.. fixture.Equipos[1].Jugadores.Skip(1)] }, .. fixture.Equipos.Skip(2)],
            Partidos = [.. fixture.Partidos.Skip(1)],
        };

        var respuesta = await Importar(cliente, "liga-a", codigo, recortado);

        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        using var doc = await Cuerpo(respuesta);
        var conflictos = doc.RootElement.GetProperty("conflictos").EnumerateArray()
            .Select(c => (Tipo: c.GetProperty("tipo").GetString(), Clave: c.GetProperty("clave").GetString())).ToList();
        Assert.Equal(2, conflictos.Count);
        Assert.Contains(conflictos, c => c.Tipo == "partido" && c.Clave!.Contains(fixture.Partidos[0].Local) && c.Clave.Contains(fixture.Partidos[0].Visitante));
        Assert.Contains(conflictos, c => c.Tipo == "inscripcion" && c.Clave == sinJugador);
        FotosIguales(antes, await Foto());
    }

    [Fact] // T-08
    public async Task T08_mover_un_jugador_a_otro_equipo_del_mismo_torneo_responde_409_y_no_cambia_la_base()
    {
        await using var fabrica = Fabrica();
        var cliente = fabrica.CreateClient();
        var (fixture, codigo) = (Basico(Prefijo()), $"T08-{Prefijo()}");
        await PrimeraImportacion(cliente, codigo, fixture);
        var antes = await Foto();
        var movido = fixture.Equipos[0].Jugadores[0];
        var cambiado = fixture with
        {
            Equipos =
            [
                fixture.Equipos[0] with { Jugadores = [.. fixture.Equipos[0].Jugadores.Skip(1)] },
                fixture.Equipos[1] with { Jugadores = [movido, .. fixture.Equipos[1].Jugadores] },
                .. fixture.Equipos.Skip(2),
            ],
        };

        var respuesta = await Importar(cliente, "liga-a", codigo, cambiado);

        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        using var doc = await Cuerpo(respuesta);
        var conflicto = Assert.Single(doc.RootElement.GetProperty("conflictos").EnumerateArray());
        Assert.Equal("jugador_en_otro_equipo", conflicto.GetProperty("tipo").GetString());
        Assert.Equal(movido.Codigo, conflicto.GetProperty("clave").GetString());
        FotosIguales(antes, await Foto());
    }

    [Fact] // T-09
    public async Task T09_tres_errores_semanticos_responden_422_con_los_tres_y_sin_detalles_de_la_base()
    {
        await using var fabrica = Fabrica();
        var cliente = fabrica.CreateClient();
        var (fixture, codigo) = (Basico(Prefijo()), $"T09-{Prefijo()}");
        var invalido = fixture with
        {
            Equipos =
            [
                fixture.Equipos[0],
                fixture.Equipos[1] with { Jugadores = [fixture.Equipos[1].Jugadores[0] with { Codigo = fixture.Equipos[0].Jugadores[0].Codigo }, .. fixture.Equipos[1].Jugadores.Skip(1)] },
                .. fixture.Equipos.Skip(2),
            ],
            Partidos =
            [
                fixture.Partidos[0] with { Visitante = "NO-EXISTE" },
                fixture.Partidos[1] with { Jornada = 0 },
                .. fixture.Partidos.Skip(2),
            ],
        };

        var respuesta = await Importar(cliente, "liga-a", codigo, invalido);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, respuesta.StatusCode);
        var texto = await respuesta.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(texto);
        var errores = doc.RootElement.GetProperty("errores").EnumerateArray()
            .Select(e => (Ruta: e.GetProperty("ruta").GetString(), Codigo: e.GetProperty("codigo").GetString())).ToList();
        Assert.Equal(3, errores.Count);
        Assert.Contains(("partidos[0].visitante", "equipo_inexistente"), errores);
        Assert.Contains(("partidos[1].jornada", "jornada_invalida"), errores);
        Assert.Contains(("equipos[1].jugadores[0].codigo", "codigo_de_jugador_duplicado"), errores);
        Assert.DoesNotMatch(new Regex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}"), texto);
        foreach (var prohibido in new[] { "constraint", "23505", "23503", "23514", "ux_", "fk_", "ak_", "Npgsql", "violates" })
            Assert.DoesNotContain(prohibido, texto, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await TorneosConCodigo(codigo));
    }

    [Theory] // T-10
    [InlineData("miembro_id")]
    [InlineData("miembro_desconocido")]
    public async Task T10_un_miembro_que_el_contrato_no_define_responde_400_y_no_escribe(string caso)
    {
        await using var fabrica = Fabrica();
        var cliente = fabrica.CreateClient();
        var (p, codigo) = (Prefijo(), $"T10-{Prefijo()}");
        var nodo = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(Basico(p), Json))!;
        if (caso == "miembro_id") nodo["equipos"]![0]!["id"] = Guid.NewGuid().ToString();
        else nodo["campoInventado"] = true;

        var respuesta = await ImportarTexto(cliente, codigo, nodo.ToJsonString());

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal(0, await TorneosConCodigo(codigo));
    }

    [Theory] // T-11
    [InlineData("mas_de_1_mib")]
    [InlineData("mas_de_1_mib_sin_content_length")]
    [InlineData("65_equipos")]
    public async Task T11_los_limites_de_tamano_responden_413_y_422(string caso)
    {
        await using var fabrica = Fabrica();
        var cliente = fabrica.CreateClient();
        var (p, codigo) = (Prefijo(), $"T11-{Prefijo()}");

        HttpResponseMessage respuesta;
        if (caso == "mas_de_1_mib_sin_content_length")
        {
            // Cuerpo troceado (chunked): sin Content-Length la primera guarda no puede rechazarlo (Spec 005 C-23).
            var grande = Basico(p) with { Nombre = new string('N', 1024 * 1024 + 512) };
            var peticion = Peticion("liga-a", codigo, string.Empty, UsuarioU1);
            peticion.Content = new StreamContent(new FlujoSinLongitud(JsonSerializer.SerializeToUtf8Bytes(grande, Json)));
            peticion.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            Assert.Null(peticion.Content.Headers.ContentLength);
            respuesta = await cliente.SendAsync(peticion);
        }
        else if (caso == "mas_de_1_mib")
        {
            var grande = Basico(p) with { Nombre = new string('N', 1024 * 1024 + 512) };
            respuesta = await ImportarTexto(cliente, codigo, JsonSerializer.Serialize(grande, Json));
        }
        else
        {
            var equipos = Enumerable.Range(1, 65).Select(i => new EquipoJ($"{p}-E{i}", $"Equipo {i}", false, [])).ToList();
            respuesta = await Importar(cliente, "liga-a", codigo, Basico(p) with { Equipos = equipos, Partidos = [] });
        }

        Assert.Equal(caso.StartsWith("mas_de_1_mib", StringComparison.Ordinal) ? HttpStatusCode.RequestEntityTooLarge : HttpStatusCode.UnprocessableEntity, respuesta.StatusCode);
        Assert.Equal(0, await TorneosConCodigo(codigo));
    }

    [Theory] // T-12
    [InlineData("sin_token", HttpStatusCode.Unauthorized)]
    [InlineData("admin_de_otra_liga", HttpStatusCode.Forbidden)]
    [InlineData("slug_inexistente", HttpStatusCode.NotFound)]
    public async Task T12_la_ruta_de_importacion_respeta_el_orden_404_401_403(string caso, HttpStatusCode esperado)
    {
        await using var fabrica = Fabrica();
        var cliente = fabrica.CreateClient();
        var cuerpo = JsonSerializer.Serialize(Basico(Prefijo()), Json);

        var peticion = caso switch
        {
            "sin_token" => Peticion("liga-a", "T12", cuerpo, null),
            "admin_de_otra_liga" => Peticion("liga-a", "T12", cuerpo, UsuarioU2),
            _ => Peticion("no-existe", "T12", cuerpo, UsuarioU1),
        };
        var respuesta = await cliente.SendAsync(peticion);

        Assert.Equal(esperado, respuesta.StatusCode);
    }

    [Fact] // T-13
    public async Task T13_dos_importaciones_identicas_en_paralelo_dan_200_o_409_sin_duplicados_y_el_reintento_da_200()
    {
        await using var fabrica = Fabrica();
        var cliente = fabrica.CreateClient();
        var (fixture, codigo) = (Basico(Prefijo()), $"T13-{Prefijo()}");

        var respuestas = await Task.WhenAll(
            Importar(cliente, "liga-a", codigo, fixture), Importar(cliente, "liga-a", codigo, fixture));

        Assert.All(respuestas, r => Assert.True(r.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict, $"Respuesta {(int)r.StatusCode}"));
        Assert.Contains(respuestas, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(1, await TorneosConCodigo(codigo));
        Assert.Equal(6, await Contar($"select count(*) from goalflow.partidos p join goalflow.torneos t on t.id = p.torneo_id where t.codigo = '{codigo}'"));
        Assert.Equal(12, await Contar($"select count(*) from goalflow.inscripciones_jugador i join goalflow.torneos t on t.id = i.torneo_id where t.codigo = '{codigo}'"));

        var reintento = await Importar(cliente, "liga-a", codigo, fixture);
        Assert.Equal(HttpStatusCode.OK, reintento.StatusCode);
        using var doc = await Cuerpo(reintento);
        Assert.Equal((0, 0, 6), Conteo(doc, "partidos"));
        Assert.Equal((0, 0, 4), Conteo(doc, "equipos"));
    }

    [Fact] // T-14
    public async Task T14_dos_ligas_con_los_mismos_codigos_importan_y_cada_una_ve_solo_sus_filas()
    {
        await using var fabrica = Fabrica();
        var cliente = fabrica.CreateClient();
        var (fixture, codigo) = (Basico(Prefijo()), $"T14-{Prefijo()}");

        var enA = await Importar(cliente, "liga-a", codigo, fixture);
        var enB = await Importar(cliente, "liga-b", codigo, fixture);

        Assert.Equal(HttpStatusCode.OK, enA.StatusCode);
        Assert.Equal(HttpStatusCode.OK, enB.StatusCode);

        await using var conexion = new NpgsqlConnection(db.CadenaApp);
        await conexion.OpenAsync();
        await using var transaccion = await conexion.BeginTransactionAsync();
        await using (var fijar = new NpgsqlCommand($"select set_config('app.tenant_id', '{LigaA.Value}', true)", conexion, transaccion))
            await fijar.ExecuteNonQueryAsync();
        foreach (var tabla in new[] { "torneos", "fases", "jugadores", "equipos_torneo", "inscripciones_jugador", "partidos" })
        {
            await using var comando = new NpgsqlCommand(
                $"select count(*) filter (where tenant_id = '{LigaA.Value}'), count(*) filter (where tenant_id <> '{LigaA.Value}') from goalflow.{tabla}",
                conexion, transaccion);
            await using var lector = await comando.ExecuteReaderAsync();
            await lector.ReadAsync();
            Assert.True(lector.GetInt64(0) > 0, $"{tabla}: la liga A no ve sus filas");
            Assert.Equal(0, lector.GetInt64(1));
        }
    }

    [Fact] // T-28
    public async Task T28_los_logs_contienen_el_codigo_del_torneo_y_ningun_nombre_del_payload()
    {
        var logs = new CapturaDeLogs();
        await using var fabrica = Fabrica(logs);
        var cliente = fabrica.CreateClient();
        var (fixture, codigo) = (Basico(Prefijo()), $"T28-{Prefijo()}");

        var respuesta = await Importar(cliente, "liga-a", codigo, fixture);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Contains(logs.Entradas, e => e.Contains("Importación de torneo", StringComparison.Ordinal) && e.Contains(codigo, StringComparison.Ordinal));
        var nombres = fixture.Equipos.Select(e => e.Nombre).Concat(fixture.Equipos.SelectMany(e => e.Jugadores).Select(j => j.Nombre)).Append(fixture.Nombre);
        foreach (var nombre in nombres)
            Assert.DoesNotContain(logs.Entradas, e => e.Contains(nombre, StringComparison.Ordinal));
    }

    [Fact] // T-29
    public async Task T29_retirar_un_equipo_y_cancelar_sus_partidos_actualiza_solo_eso_y_conserva_las_inscripciones()
    {
        await using var fabrica = Fabrica();
        var cliente = fabrica.CreateClient();
        var (fixture, codigo) = (Basico(Prefijo()), $"T29-{Prefijo()}");
        await PrimeraImportacion(cliente, codigo, fixture);
        var retirado = fixture.Equipos[3].Codigo;
        var retiro = fixture with
        {
            Equipos = [.. fixture.Equipos.Take(3), fixture.Equipos[3] with { Retirado = true }],
            Partidos = [.. fixture.Partidos.Select(m => m.Local == retirado || m.Visitante == retirado ? m with { Cancelado = true } : m)],
        };

        var respuesta = await Importar(cliente, "liga-a", codigo, retiro);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        using var doc = await Cuerpo(respuesta);
        Assert.Equal((0, 0, 1), Conteo(doc, "torneo"));
        Assert.Equal((0, 0, 4), Conteo(doc, "equipos"));
        Assert.Equal((0, 0, 12), Conteo(doc, "jugadores"));
        Assert.Equal((0, 1, 3), Conteo(doc, "participaciones"));
        Assert.Equal((0, 0, 12), Conteo(doc, "inscripciones"));
        Assert.Equal((0, 3, 3), Conteo(doc, "partidos"));
        Assert.Equal(1, await Contar($"""
            select count(*) from goalflow.equipos_torneo et join goalflow.torneos t on t.id = et.torneo_id
            where t.codigo = '{codigo}' and et.estado = 'Retirado'
            """));
        Assert.Equal(3, await Contar($"""
            select count(*) from goalflow.partidos p join goalflow.torneos t on t.id = p.torneo_id
            where t.codigo = '{codigo}' and p.estado = 'Cancelado'
            """));
        Assert.Equal(12, await Contar($"""
            select count(*) from goalflow.inscripciones_jugador i join goalflow.torneos t on t.id = i.torneo_id
            where t.codigo = '{codigo}'
            """));
    }

    [Fact] // T-30
    public async Task T30_un_equipo_retirado_con_un_partido_sin_cancelar_responde_422_nombrando_el_partido()
    {
        await using var fabrica = Fabrica();
        var cliente = fabrica.CreateClient();
        var (fixture, codigo) = (Basico(Prefijo()), $"T30-{Prefijo()}");
        var retirado = fixture.Equipos[3].Codigo;
        var invalido = fixture with
        {
            Equipos = [.. fixture.Equipos.Take(3), fixture.Equipos[3] with { Retirado = true }],
            // Todos los partidos del retirado van cancelados salvo el último de la lista (índice 5 no lo incluye; el 4 sí).
            Partidos = [.. fixture.Partidos.Select((m, i) => (m.Local == retirado || m.Visitante == retirado) && i != 4 ? m with { Cancelado = true } : m)],
        };

        var respuesta = await Importar(cliente, "liga-a", codigo, invalido);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, respuesta.StatusCode);
        using var doc = await Cuerpo(respuesta);
        var error = Assert.Single(doc.RootElement.GetProperty("errores").EnumerateArray());
        Assert.Equal("partidos[4].cancelado", error.GetProperty("ruta").GetString());
        Assert.Equal("partido_de_equipo_retirado_sin_cancelar", error.GetProperty("codigo").GetString());
        Assert.Equal(0, await TorneosConCodigo(codigo));
    }

    [Fact] // T-31
    public async Task T31_quitar_las_marcas_devuelve_la_participacion_a_Inscrito_y_los_partidos_a_Programado()
    {
        await using var fabrica = Fabrica();
        var cliente = fabrica.CreateClient();
        var (fixture, codigo) = (Basico(Prefijo()), $"T31-{Prefijo()}");
        await PrimeraImportacion(cliente, codigo, fixture);
        var retirado = fixture.Equipos[3].Codigo;
        var retiro = fixture with
        {
            Equipos = [.. fixture.Equipos.Take(3), fixture.Equipos[3] with { Retirado = true }],
            Partidos = [.. fixture.Partidos.Select(m => m.Local == retirado || m.Visitante == retirado ? m with { Cancelado = true } : m)],
        };
        Assert.Equal(HttpStatusCode.OK, (await Importar(cliente, "liga-a", codigo, retiro)).StatusCode);

        var tercera = await Importar(cliente, "liga-a", codigo, fixture);

        Assert.Equal(HttpStatusCode.OK, tercera.StatusCode);
        using var doc = await Cuerpo(tercera);
        Assert.Equal((0, 1, 3), Conteo(doc, "participaciones"));
        Assert.Equal((0, 3, 3), Conteo(doc, "partidos"));
        Assert.Equal(0, await Contar($"""
            select count(*) from goalflow.equipos_torneo et join goalflow.torneos t on t.id = et.torneo_id
            where t.codigo = '{codigo}' and et.estado = 'Retirado'
            """));
        Assert.Equal(0, await Contar($"""
            select count(*) from goalflow.partidos p join goalflow.torneos t on t.id = p.torneo_id
            where t.codigo = '{codigo}' and p.estado = 'Cancelado'
            """));
    }
}

/// <summary>Flujo que no informa su longitud ni permite posicionarse: la petición que lo envía va troceada (chunked).</summary>
internal sealed class FlujoSinLongitud(byte[] datos) : Stream
{
    private readonly MemoryStream _interno = new(datos);

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => _interno.Read(buffer, offset, count);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        _interno.ReadAsync(buffer, offset, count, cancellationToken);

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        _interno.ReadAsync(buffer, cancellationToken);

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
