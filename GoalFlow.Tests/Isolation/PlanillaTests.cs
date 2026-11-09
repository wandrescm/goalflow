using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using GoalFlow.Infrastructure.Mensajeria;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using static GoalFlow.Tests.Isolation.BaseDeAislamiento;
using static GoalFlow.Tests.Isolation.EntornoDePlanilla;

namespace GoalFlow.Tests.Isolation;

/// <summary>
/// Spec 006 T-04 a T-07, T-09 a T-13, T-20, T-22, T-24 y T-25: la planilla por la API real, contra PostgreSQL 17 como
/// goalflow_app. T-08 (concurrencia gobernada) y T-21 están en <c>ConcurrenciaDePlanillaTests</c>.
/// </summary>
[Collection(ColeccionDePlanilla.Nombre)]
public partial class PlanillaTests(BaseDeAislamiento db)
{
    [GeneratedRegex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex PatronDeGuid();

    private static async Task<(string Resultado, int Version)> Aceptada(HttpResponseMessage respuesta)
    {
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        using var cuerpo = await Cuerpo(respuesta);
        return (cuerpo.RootElement.GetProperty("resultado").GetString()!, cuerpo.RootElement.GetProperty("version").GetInt32());
    }

    private static async Task<List<(string Ruta, string Codigo)>> Errores(HttpResponseMessage respuesta, HttpStatusCode estado)
    {
        Assert.Equal(estado, respuesta.StatusCode);
        using var cuerpo = await Cuerpo(respuesta);
        return [.. cuerpo.RootElement.GetProperty("errores").EnumerateArray()
            .Select(e => (e.GetProperty("ruta").GetString()!, e.GetProperty("codigo").GetString()!))];
    }

    private static async Task<(string Codigo, int? Version)> Conflicto(HttpResponseMessage respuesta)
    {
        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        using var cuerpo = await Cuerpo(respuesta);
        var version = cuerpo.RootElement.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : (int?)null;
        return (cuerpo.RootElement.GetProperty("codigo").GetString()!, version);
    }

    private static async Task SinDetallesInternos(HttpResponseMessage respuesta)
    {
        var texto = await respuesta.Content.ReadAsStringAsync();
        Assert.DoesNotMatch(PatronDeGuid(), texto);
        Assert.DoesNotContain("fk_", texto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ux_", texto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("23503", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("23505", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("constraint", texto, StringComparison.OrdinalIgnoreCase);
    }

    // ---- T-04 ----

    [Fact]
    public async Task T04_el_primer_PUT_guarda_la_planilla_los_goles_y_las_tarjetas_y_actualiza_el_partido()
    {
        await using var e = new EntornoDePlanilla(db);
        var p = await e.Preparar();

        var respuesta = await e.Guardar(RutaDelPrimerPartido(p), Jugado3a1(p, 0));

        Assert.Equal(("guardada", 1), await Aceptada(respuesta));
        Assert.Equal(1, await e.Planillas(p));
        Assert.Equal(3, await e.Goles(p));
        Assert.Equal(2, await e.Tarjetas(p));
        Assert.Equal("Jugado", await e.EstadoDelPartido(p));
        Assert.Equal(1, await e.VersionDelPartido(p));
        var sub = await e.Texto($"select guardado_por::text from goalflow.planillas where partido_id = ({e.FiltroDePartido(p, 1, 2)})");
        Assert.Equal(UsuarioU1.ToString(), sub);
        var informadas = await e.Contar($"select count(*) from goalflow.planillas where tarjetas_informadas is true and partido_id = ({e.FiltroDePartido(p, 1, 2)})");
        Assert.Equal(1, informadas);
        Assert.Equal(1, await e.Contar($"select count(*) from goalflow.planillas where guardado_en is not null and partido_id = ({e.FiltroDePartido(p, 1, 2)})"));
    }

    [Fact]
    public async Task T04_el_autogol_se_acredita_al_equipo_local_y_nunca_al_autor()
    {
        await using var e = new EntornoDePlanilla(db);
        var p = await e.Preparar();
        await e.Guardar(RutaDelPrimerPartido(p), Jugado3a1(p, 0));

        var acreditadoAlLocal = await e.Contar($"""
            select count(*) from goalflow.goles g
            join goalflow.planillas pl on pl.id = g.planilla_id
            join goalflow.partidos pa on pa.id = pl.partido_id
            where pa.id = ({e.FiltroDePartido(p, 1, 2)}) and g.tipo = 'Autogol' and g.equipo_torneo_id = pa.local_id and g.cantidad = 1
            """);
        Assert.Equal(1, acreditadoAlLocal);
    }

    // ---- T-05 ----

    [Fact]
    public async Task T05_un_guardado_identico_y_el_reintento_con_version_vieja_no_escriben_nada()
    {
        await using var e = new EntornoDePlanilla(db);
        var p = await e.Preparar();
        var ruta = RutaDelPrimerPartido(p);
        Assert.Equal(("guardada", 1), await Aceptada(await e.Guardar(ruta, Jugado3a1(p, 0))));

        // C-31: ningún ciclo corre en el proceso y ningún otro anfitrión está vivo mientras se miden las fotos.
        var ciclo = e.Services.GetServices<IHostedService>().OfType<CicloDePostGuardado>().Single();
        Assert.Equal(1, AnfitrionesVivos);
        var antes = await e.Foto();

        var identico = await Aceptada(await e.Guardar(ruta, Jugado3a1(p, 1)));
        var reintento = await Aceptada(await e.Guardar(ruta, Jugado3a1(p, 0)));
        var despues = await e.Foto();

        Assert.Equal(("sinCambios", 1), identico);
        Assert.Equal(("sinCambios", 1), reintento);
        FotosIguales(antes, despues);
        Assert.Equal(0, ciclo.PasadasCompletadas);
        Assert.Equal(1, AnfitrionesVivos);
    }

    // ---- T-06 ----

    [Fact]
    public async Task T06_una_correccion_crea_la_version_2_sin_tocar_la_1_y_el_GET_devuelve_solo_la_2()
    {
        await using var e = new EntornoDePlanilla(db);
        var p = await e.Preparar();
        var ruta = RutaDelPrimerPartido(p);
        await e.Guardar(ruta, Jugado3a1(p, 0));

        var respuesta = await e.Guardar(ruta, Corregido3a1(p, 1));

        Assert.Equal(("guardada", 2), await Aceptada(respuesta));
        Assert.Equal(2, await e.Planillas(p));
        Assert.Equal(6, await e.Goles(p)); // 3 + 3 (C-05)
        Assert.Equal(4, await e.Tarjetas(p)); // 2 + 2: la corrección vuelve a enviar las tarjetas completas
        var v1 = await e.Contar($"""
            select count(*) from goalflow.goles g join goalflow.planillas pl on pl.id = g.planilla_id
            where pl.version = 1 and g.tipo = 'Jugador' and g.cantidad = 1 and pl.partido_id = ({e.FiltroDePartido(p, 1, 2)})
            """);
        Assert.Equal(1, v1); // el gol de J-22 sigue en la versión 1

        using var get = await Cuerpo(await e.Leer(ruta));
        Assert.Equal(2, get.RootElement.GetProperty("version").GetInt32());
        Assert.Equal(1, get.RootElement.GetProperty("goles").GetArrayLength());
        Assert.Equal(1, get.RootElement.GetProperty("golesSinAutor").GetProperty("visitante").GetInt32());
        Assert.Equal(1, get.RootElement.GetProperty("autogoles").GetArrayLength());
    }

    // ---- T-07 ----

    [Fact]
    public async Task T07_una_correccion_con_version_vieja_recibe_409_con_la_version_actual_y_no_escribe()
    {
        await using var e = new EntornoDePlanilla(db);
        var p = await e.Preparar();
        var ruta = RutaDelPrimerPartido(p);
        await e.Guardar(ruta, Jugado3a1(p, 0));
        await e.Guardar(ruta, Corregido3a1(p, 1));
        var antes = await e.Foto();

        var distinta = Jugado3a1(p, 1) with { Marcador = new(4, 1), Goles = [new(Jugador(p, 1, 1), 3), new(Jugador(p, 2, 2), 1)] };
        var respuesta = await e.Guardar(ruta, distinta);

        var (codigo, version) = await Conflicto(respuesta);
        Assert.Equal("conflicto_de_version", codigo);
        Assert.Equal(2, version);
        EntornoDePlanilla.FotosIguales(antes, await e.Foto());
    }

    // ---- T-09 ----

    [Fact]
    public async Task T09_el_viaje_de_ida_y_vuelta_de_GET_a_PUT_es_sinCambios_en_version_0_con_planilla_y_con_retiro()
    {
        await using var e = new EntornoDePlanilla(db);
        var p = await e.Preparar();
        var ruta = RutaDelPrimerPartido(p);

        // Versión 0.
        using (var v0 = await Cuerpo(await e.Leer(ruta)))
        {
            Assert.Equal(0, v0.RootElement.GetProperty("version").GetInt32());
            Assert.Equal("Programado", v0.RootElement.GetProperty("estado").GetString());
            Assert.Equal(("sinCambios", 0), await Aceptada(await e.GuardarTexto(ruta, DeGetAPut(v0))));
        }

        // La planilla de T-04.
        await e.Guardar(ruta, Jugado3a1(p, 0));
        using (var v1 = await Cuerpo(await e.Leer(ruta)))
            Assert.Equal(("sinCambios", 1), await Aceptada(await e.GuardarTexto(ruta, DeGetAPut(v1))));

        // Una planilla retirada, con su motivo de rectificación.
        Assert.Equal(("guardada", 2), await Aceptada(await e.Guardar(ruta, Retirada(1))));
        using (var v2 = await Cuerpo(await e.Leer(ruta)))
        {
            Assert.Equal("PartidoEquivocado", v2.RootElement.GetProperty("motivoRectificacion").GetString());
            Assert.Equal(("sinCambios", 2), await Aceptada(await e.GuardarTexto(ruta, DeGetAPut(v2))));
        }
    }

    [Fact]
    public async Task T09_un_cuerpo_del_GET_enviado_sin_la_transformacion_recibe_400()
    {
        await using var e = new EntornoDePlanilla(db);
        var p = await e.Preparar();
        var ruta = RutaDelPrimerPartido(p);
        var antes = await e.Foto();

        var get = await e.Leer(ruta);
        var crudo = await get.Content.ReadAsStringAsync();
        var respuesta = await e.GuardarTexto(ruta, crudo);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        EntornoDePlanilla.FotosIguales(antes, await e.Foto());
    }

    [Fact]
    public async Task T09_el_orden_de_las_filas_y_un_miembro_ausente_frente_a_uno_vacio_no_cuentan_como_cambio()
    {
        await using var e = new EntornoDePlanilla(db);
        var p = await e.Preparar();
        var ruta = RutaDelPrimerPartido(p);
        await e.Guardar(ruta, Jugado3a1(p, 0));

        var reordenada = Jugado3a1(p, 1) with
        {
            Goles = [new(Jugador(p, 2, 2), 1), new(Jugador(p, 1, 1), 2)],
            Tarjetas = [new(Jugador(p, 2, 2), "Roja", 1), new(Jugador(p, 1, 2), "Amarilla", 1)],
            GolesSinAutor = null,
        };

        Assert.Equal(("sinCambios", 1), await Aceptada(await e.Guardar(ruta, reordenada)));
        Assert.Equal(1, await e.Planillas(p));
    }

    // ---- T-10 ----

    [Fact]
    public async Task T10_un_partido_cancelado_rechaza_el_PUT_con_409_y_el_GET_devuelve_Cancelado()
    {
        await using var e = new EntornoDePlanilla(db);
        var p = await e.Preparar();
        var ruta = RutaDelPrimerPartido(p);
        var cancelado = Torneo(p) with { Partidos = [.. Torneo(p).Partidos.Select((m, i) => i == 0 ? m with { Cancelado = true } : m)] };
        Assert.Equal(HttpStatusCode.OK, (await e.Importar(p, cancelado)).StatusCode);
        var antes = await e.Foto();

        var respuesta = await e.Guardar(ruta, Jugado3a1(p, 0));

        var (codigo, _) = await Conflicto(respuesta);
        Assert.Equal("partido_cancelado", codigo);
        EntornoDePlanilla.FotosIguales(antes, await e.Foto());

        var get = await e.Leer(ruta);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        using var cuerpo = await Cuerpo(get);
        Assert.Equal("Cancelado", cuerpo.RootElement.GetProperty("estado").GetString());
        Assert.Equal(0, cuerpo.RootElement.GetProperty("version").GetInt32());
    }

    // ---- T-11 ----

    [Fact]
    public async Task T11_un_jugador_de_un_tercer_equipo_de_otro_torneo_o_desconocido_recibe_422_sin_datos_internos()
    {
        await using var e = new EntornoDePlanilla(db);
        var p = await e.Preparar();
        var q = await e.Preparar();
        var ruta = RutaDelPrimerPartido(p);
        var antes = await e.Foto();

        foreach (var jugador in new[] { Jugador(p, 3, 1), Jugador(q, 1, 1), $"{p}-NO-EXISTE" })
        {
            var sencilla = new PlanillaJ(0, "Jugado", new(0, 0), TarjetasInformadas: true, Tarjetas: [new(jugador, "Amarilla", 1)]);

            var respuesta = await e.Guardar(ruta, sencilla);

            var errores = await Errores(respuesta, HttpStatusCode.UnprocessableEntity);
            Assert.Equal([("tarjetas[0].jugador", "jugador_fuera_del_partido")], errores);
            await SinDetallesInternos(respuesta);
        }

        EntornoDePlanilla.FotosIguales(antes, await e.Foto());
    }

    [Fact]
    public async Task T11_el_422_trae_todos_los_errores_y_no_escribe()
    {
        await using var e = new EntornoDePlanilla(db);
        var p = await e.Preparar();
        var ruta = RutaDelPrimerPartido(p);
        var antes = await e.Foto();
        var invalida = new PlanillaJ(
            0, "Jugado", new(2, 0), Oficial: new(2, 0), TarjetasInformadas: true,
            Goles: [new(Jugador(p, 1, 1), 1)], Tarjetas: [new(Jugador(p, 1, 2), "Verde", 1)]);

        var errores = await Errores(await e.Guardar(ruta, invalida), HttpStatusCode.UnprocessableEntity);

        Assert.Contains(("oficial", "oficial_no_permitido"), errores);
        Assert.Contains(("tarjetas[0].color", "color_invalido"), errores);
        Assert.Contains(("marcador.local", "suma_de_goles_distinta_del_marcador"), errores);
        Assert.Equal(3, errores.Count);
        EntornoDePlanilla.FotosIguales(antes, await e.Foto());
    }

    // ---- T-12 ----

    [Fact]
    public async Task T12_las_rutas_estan_dentro_del_filtro_de_Admin_y_el_contrato_es_estricto()
    {
        await using var e = new EntornoDePlanilla(db);
        var p = await e.Preparar();
        var ruta = RutaDelPrimerPartido(p);
        var cuerpo = System.Text.Json.JsonSerializer.Serialize(Jugado3a1(p, 0), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var antes = await e.Foto();

        async Task<HttpStatusCode> Estado(string rutaPedida, string? texto, Guid? usuario, HttpMethod? metodo = null) =>
            (await e.Cliente.SendAsync(e.Peticion(metodo ?? HttpMethod.Put, rutaPedida, texto, usuario))).StatusCode;

        // Sin token: 401. Admin de la liga B en la liga A: 403. Slug desconocido: 404.
        Assert.Equal(HttpStatusCode.Unauthorized, await Estado(ruta, cuerpo, null));
        Assert.Equal(HttpStatusCode.Unauthorized, await Estado(ruta, null, null, HttpMethod.Get));
        Assert.Equal(HttpStatusCode.Forbidden, await Estado(ruta, cuerpo, UsuarioU2));
        Assert.Equal(HttpStatusCode.Forbidden, await Estado(ruta, null, UsuarioU2, HttpMethod.Get));
        Assert.Equal(HttpStatusCode.NotFound, await Estado(ruta.Replace("/liga-a/", "/liga-x/"), cuerpo, UsuarioU1));

        // Partido inexistente en la liga del llamante: 404 (la pareja al revés no existe).
        var alReves = RutaDePlanilla("liga-a", p, 1, Equipo(p, 2), Equipo(p, 1));
        Assert.Equal(HttpStatusCode.NotFound, await Estado(alReves, cuerpo, UsuarioU1));
        Assert.Equal(HttpStatusCode.NotFound, await Estado(alReves, null, UsuarioU1, HttpMethod.Get));
        Assert.Equal(HttpStatusCode.NotFound, await Estado(RutaDePlanilla("liga-a", p, 2, Equipo(p, 1), Equipo(p, 2)), cuerpo, UsuarioU1));

        // Ruta mal formada: 400.
        Assert.Equal(HttpStatusCode.BadRequest, await Estado(RutaDePlanilla("liga-a", p, 1, "mal.codigo", Equipo(p, 2)), cuerpo, UsuarioU1));
        Assert.Equal(HttpStatusCode.BadRequest, await Estado(RutaDePlanilla("liga-a", "mal.codigo", 1, Equipo(p, 1), Equipo(p, 2)), cuerpo, UsuarioU1));
        Assert.Equal(HttpStatusCode.BadRequest, await Estado(ruta.Replace("/fases/1/", "/fases/0/"), cuerpo, UsuarioU1));
        Assert.Equal(HttpStatusCode.BadRequest, await Estado(ruta.Replace("/fases/1/", "/fases/x/"), cuerpo, UsuarioU1));

        // Cuerpo: miembro desconocido, un id, JSON inválido, falta un miembro obligatorio: 400.
        Assert.Equal(HttpStatusCode.BadRequest, await Estado(ruta, cuerpo.Replace("{", "{\"inventado\":1,"), UsuarioU1));
        Assert.Equal(HttpStatusCode.BadRequest, await Estado(ruta, cuerpo.Replace("{", $"{{\"id\":\"{Guid.NewGuid()}\","), UsuarioU1));
        Assert.Equal(HttpStatusCode.BadRequest, await Estado(ruta, cuerpo.Replace("{", "{\"version\":1,"), UsuarioU1));
        Assert.Equal(HttpStatusCode.BadRequest, await Estado(ruta, "{no es json", UsuarioU1));
        Assert.Equal(HttpStatusCode.BadRequest, await Estado(ruta, "{\"estado\":\"Jugado\"}", UsuarioU1));
        Assert.Equal(HttpStatusCode.BadRequest, await Estado(ruta, cuerpo.Replace("\"versionEsperada\":0", "\"versionEsperada\":-1"), UsuarioU1));

        // Más de 64 KiB: 413.
        var grande = $"{{\"versionEsperada\":0,\"estado\":\"Programado\",\"motivo\":\"{new string('x', 70_000)}\"}}";
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, await Estado(ruta, grande, UsuarioU1));

        // En ningún caso se escribió nada.
        EntornoDePlanilla.FotosIguales(antes, await e.Foto());
    }

    // ---- T-13 ----

    [Fact]
    public async Task T13_se_puede_retirar_la_planilla_sin_perder_la_historia_y_luego_la_importacion_cancela_el_partido()
    {
        await using var e = new EntornoDePlanilla(db);
        var p = await e.Preparar();
        var ruta = RutaDelPrimerPartido(p);
        await e.Guardar(ruta, Jugado3a1(p, 0));

        var respuesta = await e.Guardar(ruta, Retirada(1));

        Assert.Equal(("guardada", 2), await Aceptada(respuesta));
        Assert.Equal("Programado", await e.EstadoDelPartido(p));
        Assert.Equal(3, await e.Goles(p)); // la historia sigue
        Assert.Equal(2, await e.Tarjetas(p));
        var guardada = await e.Texto($"""
            select motivo_rectificacion || '/' || guardado_por::text from goalflow.planillas
            where version = 2 and partido_id = ({e.FiltroDePartido(p, 1, 2)})
            """);
        Assert.Equal($"PartidoEquivocado/{UsuarioU1}", guardada);

        var cancelado = Torneo(p) with { Partidos = [.. Torneo(p).Partidos.Select((m, i) => i == 0 ? m with { Cancelado = true } : m)] };
        Assert.Equal(HttpStatusCode.OK, (await e.Importar(p, cancelado)).StatusCode);
        Assert.Equal("Cancelado", await e.EstadoDelPartido(p));

        // R-15: el GET informa Cancelado aunque la planilla vigente sea el retiro, y el PUT recibe 409.
        using var get = await Cuerpo(await e.Leer(ruta));
        Assert.Equal("Cancelado", get.RootElement.GetProperty("estado").GetString());
        Assert.Equal(2, get.RootElement.GetProperty("version").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, (await e.Guardar(ruta, Jugado3a1(p, 2))).StatusCode);
    }

    // ---- T-20 ----

    [Fact]
    public async Task T20_los_registros_llevan_el_torneo_y_la_version_y_nunca_un_nombre_de_jugador_o_equipo()
    {
        var logs = new CapturaDeLogs();
        await using var e = new EntornoDePlanilla(db, logs);
        var p = await e.Preparar();
        var ruta = RutaDelPrimerPartido(p);
        await e.Guardar(ruta, Jugado3a1(p, 0));
        await e.Guardar(ruta, Corregido3a1(p, 1));

        var entradas = logs.Entradas;
        Assert.Contains(entradas, x => x.Contains(p, StringComparison.Ordinal) && x.Contains("versión 1", StringComparison.Ordinal));
        Assert.Contains(entradas, x => x.Contains(p, StringComparison.Ordinal) && x.Contains("versión 2", StringComparison.Ordinal));
        var nombres = Torneo(p).Equipos.Select(x => x.Nombre).Concat(Torneo(p).Equipos.SelectMany(x => x.Jugadores).Select(j => j.Nombre)).Distinct();
        foreach (var nombre in nombres)
            Assert.DoesNotContain(entradas, x => x.Contains(nombre, StringComparison.Ordinal));
    }

    // ---- T-22 ----

    [Fact]
    public async Task T22_una_suspension_adjudicada_conserva_el_marcador_deportivo_y_guarda_el_oficial_aparte()
    {
        await using var e = new EntornoDePlanilla(db);
        var p = await e.Preparar();
        var ruta = RutaDelPrimerPartido(p);
        var suspendido = new PlanillaJ(
            0, "Suspendido", new(1, 0), Motivo: "Clima", TarjetasInformadas: true,
            Goles: [new(Jugador(p, 1, 1), 1)], Tarjetas: [new(Jugador(p, 2, 2), "Amarilla", 1)]);
        Assert.Equal(("guardada", 1), await Aceptada(await e.Guardar(ruta, suspendido)));
        Assert.Equal("Suspendido", await e.EstadoDelPartido(p));

        var adjudicado = suspendido with { VersionEsperada = 1, Estado = "Adjudicado", Motivo = "Sancion", Oficial = new(3, 0) };
        var respuesta = await e.Guardar(ruta, adjudicado);

        Assert.Equal(("guardada", 2), await Aceptada(respuesta));
        var v2 = $"pl.version = 2 and pl.partido_id = ({e.FiltroDePartido(p, 1, 2)})";
        Assert.Equal("1-0/3-0", await e.Texto($"select marcador_local || '-' || marcador_visitante || '/' || oficial_local || '-' || oficial_visitante from goalflow.planillas pl where {v2}"));
        Assert.Equal(1, await e.Contar($"select count(*) from goalflow.goles g join goalflow.planillas pl on pl.id = g.planilla_id where {v2}"));
        Assert.Equal(1, await e.Contar($"select count(*) from goalflow.tarjetas c join goalflow.planillas pl on pl.id = c.planilla_id where {v2}"));
        Assert.Equal(2, await e.Goles(p)); // 1 + 1: ningún gol administrativo se vuelve fila
        Assert.Equal("Adjudicado", await e.EstadoDelPartido(p));

        using var get = await Cuerpo(await e.Leer(ruta));
        Assert.Equal(1, get.RootElement.GetProperty("marcador").GetProperty("local").GetInt32());
        Assert.Equal(3, get.RootElement.GetProperty("oficial").GetProperty("local").GetInt32());
        Assert.Equal("Sancion", get.RootElement.GetProperty("motivo").GetString());
    }

    // ---- T-24 (integración) ----

    private static async Task Rectificacion422(HttpResponseMessage respuesta)
    {
        var errores = await Errores(respuesta, HttpStatusCode.UnprocessableEntity);
        Assert.Contains(("motivoRectificacion", "rectificacion_invalida"), errores);
    }

    [Fact]
    public async Task T24_el_primer_registro_y_las_correcciones_en_el_mismo_estado_no_llevan_motivo()
    {
        await using var e = new EntornoDePlanilla(db);
        var p = await e.Preparar();
        var ruta = RutaDelPrimerPartido(p);

        // Ordinaria con motivo: 422.
        await Rectificacion422(await e.Guardar(ruta, Jugado3a1(p, 0) with { MotivoRectificacion = "ErrorDeRegistro" }));
        Assert.Equal(0, await e.Planillas(p));

        // Ordinaria sin motivo: 200, y la corrección en el mismo estado también.
        Assert.Equal(("guardada", 1), await Aceptada(await e.Guardar(ruta, Jugado3a1(p, 0))));
        Assert.Equal(("guardada", 2), await Aceptada(await e.Guardar(ruta, Corregido3a1(p, 1))));
        await Rectificacion422(await e.Guardar(ruta, Jugado3a1(p, 2) with { MotivoRectificacion = "Otro" }));
    }

    [Fact]
    public async Task T24_las_transiciones_excepcionales_exigen_motivo()
    {
        await using var e = new EntornoDePlanilla(db);
        var p = await e.Preparar();
        var ruta = RutaDelPrimerPartido(p);
        await e.Guardar(ruta, Jugado3a1(p, 0));

        var suspendido = new PlanillaJ(
            1, "Suspendido", new(1, 0), Motivo: "Luz", TarjetasInformadas: true, Goles: [new(Jugador(p, 1, 1), 1)]);
        var adjudicado = new PlanillaJ(1, "Adjudicado", null, new(3, 0), "WalkOver");

        await Rectificacion422(await e.Guardar(ruta, suspendido));
        await Rectificacion422(await e.Guardar(ruta, adjudicado));
        await Rectificacion422(await e.Guardar(ruta, Vacia(1))); // retiro sin motivo
        Assert.Equal(1, await e.Planillas(p));

        Assert.Equal(("guardada", 2), await Aceptada(await e.Guardar(ruta, adjudicado with { MotivoRectificacion = "DecisionReglamentaria" })));
    }

    [Fact]
    public async Task T24_programado_a_programado_con_otro_contenido_es_invalido_y_el_reintento_identico_del_retiro_no()
    {
        await using var e = new EntornoDePlanilla(db);
        var p = await e.Preparar();
        var ruta = RutaDelPrimerPartido(p);

        await Rectificacion422(await e.Guardar(ruta, Retirada(0))); // Programado → Programado con otro contenido

        await e.Guardar(ruta, Jugado3a1(p, 0));
        Assert.Equal(("guardada", 2), await Aceptada(await e.Guardar(ruta, Retirada(1))));
        Assert.Equal(("sinCambios", 2), await Aceptada(await e.Guardar(ruta, Retirada(2))));
        Assert.Equal(("sinCambios", 2), await Aceptada(await e.Guardar(ruta, Retirada(1)))); // reintento tras respuesta perdida
        await Rectificacion422(await e.Guardar(ruta, Retirada(2, "ErrorDeRegistro"))); // solo cambia el motivo
        await Rectificacion422(await e.Guardar(ruta, Vacia(2))); // solo se quita el motivo
    }

    [Fact]
    public async Task T24_una_peticion_vieja_que_ademas_rompe_la_regla_recibe_409_y_no_422()
    {
        await using var e = new EntornoDePlanilla(db);
        var p = await e.Preparar();
        var ruta = RutaDelPrimerPartido(p);
        await e.Guardar(ruta, Jugado3a1(p, 0));

        var vieja = new PlanillaJ(0, "Adjudicado", null, new(3, 0), "WalkOver"); // excepcional sin motivo y con versión vieja

        var (codigo, version) = await Conflicto(await e.Guardar(ruta, vieja));

        Assert.Equal("conflicto_de_version", codigo);
        Assert.Equal(1, version);
    }

    [Fact]
    public async Task T24_el_motivo_pertenece_a_la_version_que_lo_guarda_y_no_se_arrastra()
    {
        await using var e = new EntornoDePlanilla(db);
        var p = await e.Preparar();
        var ruta = RutaDelPrimerPartido(p);
        await e.Guardar(ruta, Jugado3a1(p, 0));

        // Versión n = 2: Jugado → Adjudicado con DecisionReglamentaria (con marcador, para poder cambiar una tarjeta).
        var versionN = Jugado3a1(p, 1) with
        {
            Estado = "Adjudicado", Oficial = new(3, 0), Motivo = "Sancion", MotivoRectificacion = "DecisionReglamentaria",
        };
        Assert.Equal(("guardada", 2), await Aceptada(await e.Guardar(ruta, versionN)));

        // El GET enviado de vuelta sin cambios: sinCambios.
        using (var get = await Cuerpo(await e.Leer(ruta)))
            Assert.Equal(("sinCambios", 2), await Aceptada(await e.GuardarTexto(ruta, DeGetAPut(get))));

        var unaTarjetaMenos = versionN with { VersionEsperada = 2, Tarjetas = [versionN.Tarjetas![0]] };

        // Una tarjeta cambiada y el motivo conservado: 422. Solo el motivo quitado o cambiado: 422.
        await Rectificacion422(await e.Guardar(ruta, unaTarjetaMenos));
        await Rectificacion422(await e.Guardar(ruta, versionN with { VersionEsperada = 2, MotivoRectificacion = null }));
        await Rectificacion422(await e.Guardar(ruta, versionN with { VersionEsperada = 2, MotivoRectificacion = "ErrorDeRegistro" }));

        // Una tarjeta cambiada y el motivo quitado: versión 3 sin motivo; la 2 conserva el suyo.
        Assert.Equal(("guardada", 3), await Aceptada(await e.Guardar(ruta, unaTarjetaMenos with { MotivoRectificacion = null })));
        var motivos = await e.Texto($"""
            select string_agg(coalesce(motivo_rectificacion, '-'), ',' order by version) from goalflow.planillas
            where partido_id = ({e.FiltroDePartido(p, 1, 2)})
            """);
        Assert.Equal("-,DecisionReglamentaria,-", motivos);

        // Un nuevo cambio excepcional (a Jugado) sin motivo: 422; con su propio motivo: 200.
        var aJugado = Jugado3a1(p, 3);
        await Rectificacion422(await e.Guardar(ruta, aJugado));
        Assert.Equal(("guardada", 4), await Aceptada(await e.Guardar(ruta, aJugado with { MotivoRectificacion = "ErrorDeRegistro" })));
    }

    // ---- T-25 ----

    [Fact]
    public async Task T25_la_bandera_de_tarjetas_se_guarda_como_se_envia_y_cambiarla_sola_es_un_guardado_efectivo()
    {
        await using var e = new EntornoDePlanilla(db);
        var p = await e.Preparar();
        var ruta = RutaDelPrimerPartido(p);
        var sinInformar = new PlanillaJ(0, "Jugado", new(0, 0), TarjetasInformadas: false);
        Assert.Equal(("guardada", 1), await Aceptada(await e.Guardar(ruta, sinInformar)));
        using (var get = await Cuerpo(await e.Leer(ruta)))
            Assert.False(get.RootElement.GetProperty("tarjetasInformadas").GetBoolean());

        var confirmadoCero = sinInformar with { VersionEsperada = 1, TarjetasInformadas = true };
        Assert.Equal(("guardada", 2), await Aceptada(await e.Guardar(ruta, confirmadoCero)));
        using (var get = await Cuerpo(await e.Leer(ruta)))
            Assert.True(get.RootElement.GetProperty("tarjetasInformadas").GetBoolean());
        Assert.Equal(0, await e.Tarjetas(p));
        Assert.Equal(("sinCambios", 2), await Aceptada(await e.Guardar(ruta, confirmadoCero with { VersionEsperada = 2 })));
    }
}
