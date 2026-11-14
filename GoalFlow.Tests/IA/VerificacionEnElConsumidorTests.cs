using System.Text;
using GoalFlow.Application.IA;
using GoalFlow.Application.Mensajeria;
using GoalFlow.Tests.IA.Verificacion;
using GoalFlow.Tests.Isolation;
using GoalFlow.Tests.Mensajeria;
using Microsoft.Extensions.Logging;
using static GoalFlow.Tests.Isolation.BaseDeAislamiento;

namespace GoalFlow.Tests.IA;

/// <summary>
/// Spec 008 T-10 a T-12 (R-13 a R-16): el consumidor <c>ia.resumen-partido</c> verifica el texto del modelo antes de guardarlo y
/// reintenta los rechazos dentro del tope de llamadas. PostgreSQL 17 como goalflow_app y el generador falso.
/// </summary>
[Collection(ColeccionDeIa.Nombre)]
public class VerificacionEnElConsumidorTests(BaseDeAislamiento db)
{
    private const string CodigoDeRechazo = "verificacion_rechazada:v1:V3,V4";

    // Los hechos de F1 (Spec 008 Apéndice A): 2-1; goles de Jugador Uno, Dos y Tres; Jugador Cuatro amarilla; tarjetas informadas.
    private static readonly PlanillaSembrada PlanillaF1 = new(
        "Jugado",
        Marcador: (2, 1),
        TarjetasInformadas: true,
        Goles: [("Jugador Uno", 1), ("Jugador Dos", 1), ("Jugador Tres", 1)],
        Tarjetas: [("Jugador Cuatro", "Amarilla", 1)]);

    private static RespuestaGenerada Respuesta(string texto) => new(ControlDeGenerador.Json(texto), "end_turn", 2_000, 400);

    private static string TextoDe(string id) => CorpusV1.Caso(id).Texto;

    // ---- T-10 (R-13 a R-15) ----

    [Fact]
    public async Task T10_un_texto_rechazado_se_concilia_no_se_guarda_y_el_reintento_envia_la_misma_solicitud()
    {
        await using var host = AnfitrionDeIa.Construir(db);
        var p = await PartidoSembrado.CrearAsync(db, LigaA);
        await p.GuardarAsync(PlanillaF1);
        host.Control.AlGenerar = n => Task.FromResult(Respuesta(n == 1 ? TextoDe("M6") : TextoDe("C1")));

        await Assert.ThrowsAsync<FalloReintentableException>(() => host.ProcesarAsync(p, 1, 1));

        // Tras el rechazo: nada guardado, la llamada 1 conciliada con el código del rechazo y su uso reportado.
        Assert.Equal(0, await host.Resumenes(p));
        var primera = Assert.Single(await host.LlamadasDe(p));
        Assert.Equal((1, "Conciliada", (int?)2_000, (int?)400, (string?)CodigoDeRechazo), (primera.Numero, primera.Estado, primera.Entrada, primera.Salida, primera.Resultado));
        Assert.True(primera.Costo > 0);
        Assert.Equal(primera.Costo, (await host.Contador(LigaA, host.MesActual)).Consumido);

        await host.ProcesarAsync(p, 1, 2); // el intento 2 completa

        var llamadas = await host.LlamadasDe(p);
        Assert.Equal([1, 2], llamadas.Select(l => l.Numero));
        Assert.Equal([CodigoDeRechazo, "exito"], llamadas.Select(l => l.Resultado));
        Assert.All(llamadas, l => Assert.Equal("Conciliada", l.Estado));
        var resumen = Assert.Single(await host.ResumenesDe(p));
        Assert.Equal(("Modelo", "PorVerificar", TextoDe("C1"), 1), (resumen.Origen, resumen.Estado, resumen.Texto, resumen.Version));
        Assert.Equal(2, host.Control.Generaciones);

        // R-15: la misma solicitud, byte a byte, las dos veces.
        var solicitudes = host.Control.SolicitudesDeGeneracion;
        Assert.Equal(2, solicitudes.Count);
        Assert.Equal(Encoding.UTF8.GetBytes(solicitudes[0].TextoDeSistema), Encoding.UTF8.GetBytes(solicitudes[1].TextoDeSistema));
        Assert.Equal(Encoding.UTF8.GetBytes(solicitudes[0].EntradaCanonica), Encoding.UTF8.GetBytes(solicitudes[1].EntradaCanonica));
        Assert.Equal(solicitudes[0], solicitudes[1]);
    }

    // ---- T-11 (R-14): el rechazo se acota con el tope de llamadas ----

    [Fact]
    public async Task T11_un_texto_siempre_rechazado_cuesta_tres_llamadas_y_el_cuarto_intento_guarda_el_borrador_sin_llamar()
    {
        await using var host = AnfitrionDeIa.Construir(db);
        var p = await PartidoSembrado.CrearAsync(db, LigaA);
        await p.GuardarAsync(PlanillaF1);
        host.Control.AlGenerar = _ => Task.FromResult(Respuesta(TextoDe("M6")));

        for (var intento = 1; intento <= 3; intento++)
        {
            await Assert.ThrowsAsync<FalloReintentableException>(() => host.ProcesarAsync(p, 1, intento));

            Assert.Equal(intento, host.Control.Generaciones);
            var llamadas = await host.LlamadasDe(p);
            Assert.Equal(Enumerable.Range(1, intento), llamadas.Select(l => l.Numero));
            Assert.All(llamadas, l => Assert.Equal((CodigoDeRechazo, "Conciliada"), (l.Resultado, l.Estado)));
            Assert.Equal(0, await host.Resumenes(p));
        }

        // El cuarto intento encuentra 3 llamadas guardadas: no llama y guarda el borrador del tope.
        await host.ProcesarAsync(p, 1, 4);

        Assert.Equal(3, host.Control.Generaciones);
        Assert.Equal(3, await host.Llamadas(p));
        var resumen = Assert.Single(await host.ResumenesDe(p));
        Assert.Equal(("Plantilla", "Borrador", "limite_de_llamadas"), (resumen.Origen, resumen.Estado, resumen.Motivo));
        Assert.DoesNotContain(await host.ResumenesDe(p), r => r.Origen == "Modelo");
    }

    // ---- T-12 (R-16): el registro del rechazo ----

    [Fact]
    public async Task T12_el_registro_del_rechazo_lleva_la_version_y_las_reglas_y_nunca_el_texto_ni_los_nombres()
    {
        var registros = new CapturaDeRegistros();
        await using var host = AnfitrionDeIa.Construir(db, registros: registros);
        const string sufijo = " MARCA-N-7";
        var p = await PartidoSembrado.CrearAsync(db, LigaA, sufijo);
        await p.GuardarAsync(PlanillaF1);
        host.Control.AlGenerar = _ => Task.FromResult(Respuesta(TextoDe("M6") + " MARCA-T-5 marcada."));

        await Assert.ThrowsAsync<FalloReintentableException>(() => host.ProcesarAsync(p, 1, 1));

        var llamada = Assert.Single(await host.LlamadasDe(p));
        Assert.StartsWith("verificacion_rechazada:v1:", llamada.Resultado);
        var reglas = llamada.Resultado!["verificacion_rechazada:v1:".Length..].Split(',');
        Assert.NotEmpty(reglas);

        var todo = string.Join('\n', registros.Entradas.Select(e => e.Texto));
        Assert.DoesNotContain("MARCA-N-7", todo); // nombres
        Assert.DoesNotContain("MARCA-T-5", todo); // texto
        Assert.DoesNotContain("sin incidencias oficiales", todo); // palabras del texto rechazado
        Assert.DoesNotContain("descontó", todo);

        var rechazo = registros.Entradas.Where(e => e.Nivel == LogLevel.Warning && e.Texto.Contains("verificación rechazada", StringComparison.Ordinal)).ToList();
        var entrada = Assert.Single(rechazo);
        Assert.Contains("VersionDelVerificador=v1", entrada.Texto);
        foreach (var regla in reglas) Assert.Contains(regla, entrada.Texto);
        Assert.Contains("Intento=1", entrada.Texto);
        Assert.Contains("Numero=1", entrada.Texto);
    }
}
