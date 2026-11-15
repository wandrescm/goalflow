using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using static GoalFlow.Tests.Isolation.BaseDeAislamiento;

namespace GoalFlow.Tests.Isolation;

/// <summary>
/// Spec 009 T-09 (parte de integración) y T-10: el reglamento versionado en la importación, por la API real contra
/// PostgreSQL. Misma colección que la importación: la foto de T-10 mide todas las tablas y nadie más debe escribir.
/// </summary>
[Collection(ColeccionDeImportacion.Nombre)]
public class ReglamentoEnImportacionTests(BaseDeAislamiento db)
{
    private static readonly LlaveDePrueba Llave = new();

    private WebApplicationFactory<Program> Fabrica() => AnfitrionDeAdmin.Crear(db, new FuenteDeJwksDePrueba(Llave), null);

    private static string Prefijo() => Guid.NewGuid().ToString("N")[..6];

    /// <summary>Un fixture mínimo válido: una fase, dos equipos y un partido. Los códigos llevan un prefijo único por prueba.</summary>
    private static JsonObject Fixture(string p) => new()
    {
        ["nombre"] = "Torneo Reglamento",
        ["fases"] = new JsonArray(new JsonObject { ["orden"] = 1, ["tipo"] = "TodosContraTodos", ["vueltas"] = 1 }),
        ["equipos"] = new JsonArray(
            new JsonObject { ["codigo"] = $"{p}-E1", ["nombre"] = "Equipo Uno", ["retirado"] = false, ["jugadores"] = new JsonArray() },
            new JsonObject { ["codigo"] = $"{p}-E2", ["nombre"] = "Equipo Dos", ["retirado"] = false, ["jugadores"] = new JsonArray() }),
        ["partidos"] = new JsonArray(new JsonObject
        {
            ["fase"] = 1, ["jornada"] = 1, ["local"] = $"{p}-E1", ["visitante"] = $"{p}-E2", ["cancelado"] = false,
        }),
    };

    private static JsonObject Motivo(int ganador = 3, int perdedor = 0, int empate = 1) => new()
    {
        ["puntosGanador"] = ganador, ["puntosPerdedor"] = perdedor, ["puntosEmpate"] = empate,
        ["deduccionPerdedor"] = 0, ["deduccionEmpate"] = 0, ["cuentaComoJugado"] = true,
        ["golesOficialesEnTabla"] = true, ["golesOficialesEnValla"] = true,
        ["golesDeportivosEnGoleadores"] = true, ["tarjetasEnFairPlay"] = true,
    };

    private static JsonObject Reglamento(string estado = "Provisional") => new()
    {
        ["estado"] = estado,
        ["puntos"] = new JsonObject { ["victoria"] = 3, ["empate"] = 1, ["derrota"] = 0 },
        ["desempate"] = new JsonObject
        {
            ["criterios"] = new JsonArray("PuntosDirectos", "DiferenciaDeGolesDirecta", "DiferenciaDeGoles", "FairPlay"),
            ["directoRecursivo"] = true,
        },
        ["fairPlay"] = new JsonObject { ["amarilla"] = 1, ["azul"] = 2, ["roja"] = 3 },
        ["adjudicacion"] = new JsonObject { ["WalkOver"] = Motivo() },
    };

    private static JsonObject Con(JsonObject fixture, JsonObject? reglamento)
    {
        var copia = (JsonObject)JsonNode.Parse(fixture.ToJsonString())!;
        if (reglamento is not null) copia["reglamento"] = JsonNode.Parse(reglamento.ToJsonString());
        return copia;
    }

    private Task<HttpResponseMessage> Importar(HttpClient cliente, string torneo, JsonObject cuerpo)
    {
        var peticion = new HttpRequestMessage(HttpMethod.Put, $"/liga-a/admin/torneos/{torneo}")
        {
            Content = new StringContent(cuerpo.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        peticion.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TokensDePrueba.Firmar(Llave, UsuarioU1));
        return cliente.SendAsync(peticion);
    }

    private static async Task<(HttpStatusCode Estado, string Texto, JsonDocument? Doc)> Leer(HttpResponseMessage respuesta)
    {
        var texto = await respuesta.Content.ReadAsStringAsync();
        return (respuesta.StatusCode, texto, texto.Length > 0 && texto[0] == '{' ? JsonDocument.Parse(texto) : null);
    }

    private static (int Creados, int Actualizados, int SinCambios) Conteo(JsonDocument doc, string coleccion)
    {
        var c = doc.RootElement.GetProperty(coleccion);
        return (c.GetProperty("creados").GetInt32(), c.GetProperty("actualizados").GetInt32(), c.GetProperty("sinCambios").GetInt32());
    }

    private sealed record FilaDeReglamento(int Version, string Estado, Guid GuardadoPor, string Contenido);

    private async Task<List<FilaDeReglamento>> Versiones(string torneo)
    {
        await using var conexion = new NpgsqlConnection(db.CadenaSuperusuario);
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand($"""
            select r.version, r.estado, r.guardado_por, r.contenido::text
            from goalflow.reglamentos_torneo r join goalflow.torneos t on t.id = r.torneo_id
            where t.codigo = '{torneo}' order by r.version
            """, conexion);
        await using var lector = await comando.ExecuteReaderAsync();
        var filas = new List<FilaDeReglamento>();
        while (await lector.ReadAsync())
            filas.Add(new FilaDeReglamento(lector.GetInt32(0), lector.GetString(1), lector.GetGuid(2), lector.GetString(3)));
        return filas;
    }

    private async Task<long> Torneos(string codigo)
    {
        await using var conexion = new NpgsqlConnection(db.CadenaSuperusuario);
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand($"select count(*) from goalflow.torneos where codigo = '{codigo}'", conexion);
        return Convert.ToInt64(await comando.ExecuteScalarAsync());
    }

    /// <summary>Foto de todas las tablas del schema como superusuario: filas y multiconjunto de xmin (Spec 005 C-15).</summary>
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

        Assert.Contains("reglamentos_torneo", foto.Keys);
        return foto;
    }

    private static void FotosIguales(Dictionary<string, string[]> antes, Dictionary<string, string[]> despues)
    {
        Assert.Equal(antes.Keys.Order(), despues.Keys.Order());
        foreach (var (tabla, versiones) in antes)
            Assert.True(versiones.SequenceEqual(despues[tabla]), $"La tabla {tabla} cambió (filas o xmin)");
    }

    // ---- T-10 ----

    [Fact] // T-10, R-07 (b), R-08
    public async Task T10_una_importacion_con_reglamento_crea_la_version_1_con_el_sub_del_admin()
    {
        await using var fabrica = Fabrica();
        var cliente = fabrica.CreateClient();
        var (p, codigo) = (Prefijo(), $"R10-{Prefijo()}");

        var (estado, texto, doc) = await Leer(await Importar(cliente, codigo, Con(Fixture(p), Reglamento())));

        Assert.Equal(HttpStatusCode.OK, estado);
        Assert.Equal((1, 0, 0), Conteo(doc!, "reglamento"));
        var fila = Assert.Single(await Versiones(codigo));
        Assert.Equal(1, fila.Version);
        Assert.Equal("Provisional", fila.Estado);
        Assert.Equal(UsuarioU1, fila.GuardadoPor);
        // R-03: el contenido es la forma canónica sin el estado.
        using var contenido = JsonDocument.Parse(fila.Contenido);
        Assert.False(contenido.RootElement.TryGetProperty("estado", out _));
        Assert.Equal(3, contenido.RootElement.GetProperty("puntos").GetProperty("victoria").GetInt32());
        Assert.Equal(3, contenido.RootElement.GetProperty("adjudicacion").GetProperty("WalkOver").GetProperty("puntosGanador").GetInt32());
        // R-10: el sub nunca sale en la respuesta.
        Assert.DoesNotContain(UsuarioU1.ToString(), texto, StringComparison.OrdinalIgnoreCase);
    }

    [Fact] // T-10, R-07 (a), R-08: sin reglamento y sin reglamento guardado no se escribe nada y todo va en cero
    public async Task T10_una_importacion_sin_reglamento_y_sin_reglamento_guardado_cuenta_cero_y_no_escribe()
    {
        await using var fabrica = Fabrica();
        var cliente = fabrica.CreateClient();
        var (p, codigo) = (Prefijo(), $"R10A-{Prefijo()}");

        var (estado, _, doc) = await Leer(await Importar(cliente, codigo, Fixture(p)));

        Assert.Equal(HttpStatusCode.OK, estado);
        Assert.Equal((0, 0, 0), Conteo(doc!, "reglamento"));
        Assert.Empty(await Versiones(codigo));
    }

    [Fact] // T-10, R-07 (c) y (d), C-14
    public async Task T10_la_importacion_identica_no_escribe_un_cambio_de_estado_crea_la_version_2_y_omitir_da_409()
    {
        await using var fabrica = Fabrica();
        var cliente = fabrica.CreateClient();
        var (p, codigo) = (Prefijo(), $"R10B-{Prefijo()}");
        var fixture = Fixture(p);
        Assert.Equal(HttpStatusCode.OK, (await Importar(cliente, codigo, Con(fixture, Reglamento()))).StatusCode);
        var version1 = Assert.Single(await Versiones(codigo));

        // (c) idéntica: nada se escribe, ni una fila ni un xmin de ninguna tabla.
        var antes = await Foto();
        var (estadoIdentica, _, identica) = await Leer(await Importar(cliente, codigo, Con(fixture, Reglamento())));
        Assert.Equal(HttpStatusCode.OK, estadoIdentica);
        Assert.Equal((0, 0, 1), Conteo(identica!, "reglamento"));
        FotosIguales(antes, await Foto());

        // (d) solo cambia el estado: versión 2, con el sub del Admin; la versión 1 no se toca.
        var (estadoNueva, _, nueva) = await Leer(await Importar(cliente, codigo, Con(fixture, Reglamento("Confirmado"))));
        Assert.Equal(HttpStatusCode.OK, estadoNueva);
        Assert.Equal((0, 1, 0), Conteo(nueva!, "reglamento"));
        var versiones = await Versiones(codigo);
        Assert.Equal([1, 2], versiones.Select(v => v.Version));
        Assert.Equal(version1, versiones[0]);
        Assert.Equal("Confirmado", versiones[1].Estado);
        Assert.Equal(UsuarioU1, versiones[1].GuardadoPor);

        // (e) omitir el reglamento nunca lo borra: 409 con el conflicto «reglamento» y nada escrito.
        var despuesDeLaVersion2 = await Foto();
        var (estadoOmitido, textoOmitido, omitido) = await Leer(await Importar(cliente, codigo, fixture));
        Assert.Equal(HttpStatusCode.Conflict, estadoOmitido);
        var conflicto = Assert.Single(omitido!.RootElement.GetProperty("conflictos").EnumerateArray());
        Assert.Equal("reglamento", conflicto.GetProperty("tipo").GetString());
        Assert.DoesNotContain(UsuarioU1.ToString(), textoOmitido, StringComparison.OrdinalIgnoreCase);
        FotosIguales(despuesDeLaVersion2, await Foto());
    }

    [Fact] // T-10, R-07 (c): el orden de las llaves y de los miembros no importa; el de los criterios sí
    public async Task T10_la_comparacion_ignora_el_orden_de_los_miembros_y_distingue_el_orden_de_los_criterios()
    {
        await using var fabrica = Fabrica();
        var cliente = fabrica.CreateClient();
        var (p, codigo) = (Prefijo(), $"R10C-{Prefijo()}");
        var fixture = Fixture(p);
        Assert.Equal(HttpStatusCode.OK, (await Importar(cliente, codigo, Con(fixture, Reglamento()))).StatusCode);

        // Mismos valores, miembros en otro orden: sin cambios.
        var reordenado = new JsonObject
        {
            ["adjudicacion"] = new JsonObject { ["WalkOver"] = Motivo() },
            ["fairPlay"] = new JsonObject { ["roja"] = 3, ["azul"] = 2, ["amarilla"] = 1 },
            ["desempate"] = new JsonObject
            {
                ["directoRecursivo"] = true,
                ["criterios"] = new JsonArray("PuntosDirectos", "DiferenciaDeGolesDirecta", "DiferenciaDeGoles", "FairPlay"),
            },
            ["puntos"] = new JsonObject { ["derrota"] = 0, ["empate"] = 1, ["victoria"] = 3 },
            ["estado"] = "Provisional",
        };
        var (_, _, igual) = await Leer(await Importar(cliente, codigo, Con(fixture, reordenado)));
        Assert.Equal((0, 0, 1), Conteo(igual!, "reglamento"));

        // Los mismos criterios en otro orden son otro reglamento.
        var otroOrden = Reglamento();
        otroOrden["desempate"]!["criterios"] = new JsonArray("DiferenciaDeGoles", "FairPlay");
        var (_, _, distinto) = await Leer(await Importar(cliente, codigo, Con(fixture, otroOrden)));
        Assert.Equal((0, 1, 0), Conteo(distinto!, "reglamento"));
        Assert.Equal(2, (await Versiones(codigo)).Count);
    }

    // ---- T-09 (integración) ----

    [Theory] // T-09, R-05, R-06: un miembro desconocido, incluso un identificador o una versión, o un motivo desconocido, es 400
    [InlineData("adjudicacion", "Forfeit")]
    [InlineData("reglamento", "version")]
    [InlineData("reglamento", "id")]
    [InlineData("puntos", "bonus")]
    public async Task T09_un_miembro_desconocido_del_reglamento_da_400_y_no_escribe(string donde, string miembro)
    {
        await using var fabrica = Fabrica();
        var cliente = fabrica.CreateClient();
        var (p, codigo) = (Prefijo(), $"R09-{Prefijo()}");
        var reglamento = Reglamento();
        var destino = donde == "reglamento" ? reglamento : (JsonObject)reglamento[donde]!;
        destino[miembro] = donde == "adjudicacion" ? Motivo() : JsonValue.Create(1);

        var respuesta = await Importar(cliente, codigo, Con(Fixture(p), reglamento));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal(0, await Torneos(codigo));

        // Control positivo (D-A5): el mismo cuerpo sin el miembro desconocido no da 400, así que el 400 se debe a ese miembro.
        // Estos casos son «verde inicial»: antes de la implementación el 400 lo daba el miembro «reglamento» entero.
        var (p2, codigo2) = (Prefijo(), $"R09C-{Prefijo()}");
        var control = await Importar(cliente, codigo2, Con(Fixture(p2), Reglamento()));
        Assert.Equal(HttpStatusCode.OK, control.StatusCode);
    }

    [Fact] // T-09, R-06: 422 con todos los errores del reglamento y del fixture juntos, cada uno con su ruta, sin escribir
    public async Task T09_un_reglamento_invalido_da_422_con_todos_los_errores_y_no_escribe()
    {
        await using var fabrica = Fabrica();
        var cliente = fabrica.CreateClient();
        var (p, codigo) = (Prefijo(), $"R09B-{Prefijo()}");
        var fixture = Fixture(p);
        fixture["nombre"] = "";                                  // error del fixture
        var reglamento = Reglamento("Oficial");                  // (a)
        reglamento["fairPlay"]!["roja"] = 101;                   // (h)

        var (estado, texto, doc) = await Leer(await Importar(cliente, codigo, Con(fixture, reglamento)));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, estado);
        var errores = doc!.RootElement.GetProperty("errores").EnumerateArray()
            .Select(e => (e.GetProperty("ruta").GetString(), e.GetProperty("codigo").GetString())).ToHashSet();
        Assert.Contains(("nombre", "nombre_invalido"), errores);
        Assert.Contains(("reglamento.estado", "estado_invalido"), errores);
        Assert.Contains(("reglamento.fairPlay.roja", "valor_fuera_de_rango"), errores);
        Assert.Equal(3, errores.Count);
        Assert.Equal(0, await Torneos(codigo));
        Assert.DoesNotContain(UsuarioU1.ToString(), texto, StringComparison.OrdinalIgnoreCase);
    }
}
