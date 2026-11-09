using System.Net;
using System.Text.Json;
using static GoalFlow.Tests.Isolation.EntornoDePlanilla;

namespace GoalFlow.Tests.Isolation;

/// <summary>
/// Spec 006 T-17 (R-26 a R-30, C-10): la importación después de que existen resultados. El fixture original se reenvía tal
/// cual y no borra ni reescribe un resultado; solo los cambios de calendario que la Spec permite pasan.
/// </summary>
[Collection(ColeccionDePlanilla.Nombre)]
public class ImportacionConResultadosTests(BaseDeAislamiento db)
{
    private static TorneoJ ConPartido(TorneoJ fixture, int indice, Func<PartidoJ, PartidoJ> cambio) =>
        fixture with { Partidos = [.. fixture.Partidos.Select((m, i) => i == indice ? cambio(m) : m)] };

    private static async Task<List<(string Tipo, string Clave)>> Conflictos(HttpResponseMessage respuesta)
    {
        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        using var cuerpo = await Cuerpo(respuesta);
        return [.. cuerpo.RootElement.GetProperty("conflictos").EnumerateArray()
            .Select(c => (c.GetProperty("tipo").GetString()!, c.GetProperty("clave").GetString()!))];
    }

    private static async Task<(int Creados, int Actualizados, int SinCambios)> ConteoDePartidos(HttpResponseMessage respuesta)
    {
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        using var cuerpo = await Cuerpo(respuesta);
        var c = cuerpo.RootElement.GetProperty("partidos");
        return (c.GetProperty("creados").GetInt32(), c.GetProperty("actualizados").GetInt32(), c.GetProperty("sinCambios").GetInt32());
    }

    // ---- T-17 (a) y (f): reenviar el fixture original tras un resultado no cambia nada ----

    [Fact]
    public async Task T17a_reenviar_el_fixture_original_despues_de_una_planilla_Jugado_no_cambia_nada_y_el_partido_sigue_Jugado()
    {
        await using var e = new EntornoDePlanilla(db);
        var p = await e.Preparar();
        await e.Guardar(RutaDelPrimerPartido(p), Jugado3a1(p, 0));
        var antes = await e.Foto();

        var respuesta = await e.Importar(p, Torneo(p));

        Assert.Equal((0, 0, 6), await ConteoDePartidos(respuesta));
        Assert.Equal("Jugado", await e.EstadoDelPartido(p));
        Assert.Equal(1, await e.VersionDelPartido(p));
        FotosIguales(antes, await e.Foto());
    }

    [Fact]
    public async Task T17a_un_partido_Suspendido_o_Adjudicado_tampoco_vuelve_a_Programado_con_el_fixture_original()
    {
        await using var e = new EntornoDePlanilla(db);
        var p = await e.Preparar();
        await e.Guardar(RutaDelPrimerPartido(p), new PlanillaJ(
            0, "Suspendido", new(1, 0), Motivo: "Clima", TarjetasInformadas: true, Goles: [new(Jugador(p, 1, 1), 1)]));
        var segunda = RutaDePlanilla("liga-a", p, 1, Equipo(p, 3), Equipo(p, 4));
        await e.Guardar(segunda, new PlanillaJ(0, "Adjudicado", null, new(3, 0), "WalkOver"));
        var antes = await e.Foto();

        var respuesta = await e.Importar(p, Torneo(p));

        Assert.Equal((0, 0, 6), await ConteoDePartidos(respuesta));
        Assert.Equal("Suspendido", await e.EstadoDelPartido(p));
        Assert.Equal("Adjudicado", await e.EstadoDelPartido(p, 3, 4));
        FotosIguales(antes, await e.Foto());
    }

    [Fact]
    public async Task T17a_reactivar_un_partido_Cancelado_sigue_funcionando()
    {
        await using var e = new EntornoDePlanilla(db);
        var p = await e.Preparar();
        Assert.Equal(HttpStatusCode.OK, (await e.Importar(p, ConPartido(Torneo(p), 0, m => m with { Cancelado = true }))).StatusCode);
        Assert.Equal("Cancelado", await e.EstadoDelPartido(p));

        var respuesta = await e.Importar(p, Torneo(p));

        Assert.Equal((0, 1, 5), await ConteoDePartidos(respuesta));
        Assert.Equal("Programado", await e.EstadoDelPartido(p));
    }

    // ---- T-17 (b): cancelar un partido con resultado ----

    [Fact]
    public async Task T17b_cancelar_un_partido_Jugado_recibe_409_partido_con_resultado_y_no_escribe_nada()
    {
        await using var e = new EntornoDePlanilla(db);
        var p = await e.Preparar();
        await e.Guardar(RutaDelPrimerPartido(p), Jugado3a1(p, 0));
        var antes = await e.Foto();

        var respuesta = await e.Importar(p, ConPartido(Torneo(p), 0, m => m with { Cancelado = true }));

        Assert.Equal([("partido_con_resultado", $"1/{Equipo(p, 1)}/{Equipo(p, 2)}")], await Conflictos(respuesta));
        Assert.Equal("Jugado", await e.EstadoDelPartido(p));
        FotosIguales(antes, await e.Foto());
    }

    // ---- T-17 (c): jornada de un partido con resultado, hora de uno Jugado o Adjudicado ----

    [Fact]
    public async Task T17c_cambiar_la_jornada_de_un_partido_Jugado_o_la_hora_de_un_Adjudicado_recibe_409()
    {
        await using var e = new EntornoDePlanilla(db);
        var p = await e.Preparar();
        await e.Guardar(RutaDelPrimerPartido(p), Jugado3a1(p, 0));
        await e.Guardar(RutaDePlanilla("liga-a", p, 1, Equipo(p, 3), Equipo(p, 4)), new PlanillaJ(0, "Adjudicado", null, new(3, 0), "WalkOver"));
        var antes = await e.Foto();

        var jornada = await e.Importar(p, ConPartido(Torneo(p), 0, m => m with { Jornada = 5 }));
        var hora = await e.Importar(p, ConPartido(Torneo(p), 1, m => m with { ProgramadoEn = "2026-12-01T10:00:00-05:00" }));
        var horaDelJugado = await e.Importar(p, ConPartido(Torneo(p), 0, m => m with { ProgramadoEn = "2026-12-01T10:00:00-05:00" }));

        Assert.Equal([("partido_con_resultado", $"1/{Equipo(p, 1)}/{Equipo(p, 2)}")], await Conflictos(jornada));
        Assert.Equal([("partido_con_resultado", $"1/{Equipo(p, 3)}/{Equipo(p, 4)}")], await Conflictos(hora));
        Assert.Equal([("partido_con_resultado", $"1/{Equipo(p, 1)}/{Equipo(p, 2)}")], await Conflictos(horaDelJugado));
        FotosIguales(antes, await e.Foto());
    }

    // ---- T-17 (d): la hora de un partido Suspendido sí puede cambiar ----

    [Fact]
    public async Task T17d_la_hora_de_un_partido_Suspendido_se_puede_cambiar_y_su_jornada_no()
    {
        await using var e = new EntornoDePlanilla(db);
        var p = await e.Preparar();
        var suspendido = new PlanillaJ(
            0, "Suspendido", new(1, 0), Motivo: "Luz", TarjetasInformadas: true, Goles: [new(Jugador(p, 1, 1), 1)]);
        await e.Guardar(RutaDelPrimerPartido(p), suspendido);
        var planillasAntes = await e.Planillas(p);

        var nuevaHora = await e.Importar(p, ConPartido(Torneo(p), 0, m => m with { ProgramadoEn = "2026-12-01T10:00:00-05:00" }));

        Assert.Equal((0, 1, 5), await ConteoDePartidos(nuevaHora));
        Assert.Equal("Suspendido", await e.EstadoDelPartido(p));
        Assert.Equal(1, await e.VersionDelPartido(p));
        Assert.Equal(planillasAntes, await e.Planillas(p));
        Assert.Equal(1, await e.Contar($"select jornada from goalflow.partidos where id = ({e.FiltroDePartido(p, 1, 2)})"));
        Assert.Equal(1, await e.Contar($"""
            select count(*) from goalflow.partidos where programado_en = timestamptz '2026-12-01T10:00:00-05:00'
            and id = ({e.FiltroDePartido(p, 1, 2)})
            """));

        var antes = await e.Foto();
        var otraJornada = await e.Importar(p, ConPartido(Torneo(p), 0, m => m with { Jornada = 5, ProgramadoEn = "2026-12-01T10:00:00-05:00" }));

        Assert.Equal([("partido_con_resultado", $"1/{Equipo(p, 1)}/{Equipo(p, 2)}")], await Conflictos(otraJornada));
        FotosIguales(antes, await e.Foto());
    }

    // ---- T-17 (e): retirar un equipo con resultados ----

    [Fact]
    public async Task T17e_retirar_un_equipo_con_un_partido_Jugado_recibe_409_retiro_con_resultados_y_no_escribe_nada()
    {
        await using var e = new EntornoDePlanilla(db);
        var p = await e.Preparar();
        await e.Guardar(RutaDelPrimerPartido(p), Jugado3a1(p, 0));
        var antes = await e.Foto();

        // EQ-01 se retira y, como exige el fixture, todos sus partidos se marcan cancelados.
        var fixture = Torneo(p);
        var retirado = fixture with
        {
            Equipos = [.. fixture.Equipos.Select(x => x.Codigo == Equipo(p, 1) ? x with { Retirado = true } : x)],
            Partidos = [.. fixture.Partidos.Select(m => m.Local == Equipo(p, 1) || m.Visitante == Equipo(p, 1) ? m with { Cancelado = true } : m)],
        };

        var respuesta = await e.Importar(p, retirado);

        Assert.Equal([("retiro_con_resultados", Equipo(p, 1))], await Conflictos(respuesta));
        Assert.Equal("Jugado", await e.EstadoDelPartido(p));
        FotosIguales(antes, await e.Foto());
    }

    [Fact]
    public async Task T17e_retirar_un_equipo_sin_resultados_sigue_funcionando()
    {
        await using var e = new EntornoDePlanilla(db);
        var p = await e.Preparar();
        var fixture = Torneo(p);
        var retirado = fixture with
        {
            Equipos = [.. fixture.Equipos.Select(x => x.Codigo == Equipo(p, 4) ? x with { Retirado = true } : x)],
            Partidos = [.. fixture.Partidos.Select(m => m.Local == Equipo(p, 4) || m.Visitante == Equipo(p, 4) ? m with { Cancelado = true } : m)],
        };

        var respuesta = await e.Importar(p, retirado);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal("Programado", await e.EstadoDelPartido(p));
        Assert.Equal("Cancelado", await e.EstadoDelPartido(p, 3, 4));
    }

    // ---- R-30: la importación nunca toca la versión ni las tablas de la planilla ----

    [Fact]
    public async Task T17_una_importacion_que_cambia_el_calendario_no_cambia_la_version_ni_escribe_planillas()
    {
        await using var e = new EntornoDePlanilla(db);
        var p = await e.Preparar();
        await e.Guardar(RutaDelPrimerPartido(p), Jugado3a1(p, 0));
        var antes = await e.Foto();

        var respuesta = await e.Importar(p, ConPartido(Torneo(p), 1, m => m with { Jornada = 6, ProgramadoEn = "2026-12-02T09:00:00-05:00" }));

        Assert.Equal((0, 1, 5), await ConteoDePartidos(respuesta));
        var despues = await e.Foto();
        foreach (var tabla in new[] { "planillas", "goles", "tarjetas" })
            Assert.True(antes[tabla].SequenceEqual(despues[tabla]), $"La importación escribió en {tabla}");
        Assert.Equal(1, await e.VersionDelPartido(p));
    }
}
