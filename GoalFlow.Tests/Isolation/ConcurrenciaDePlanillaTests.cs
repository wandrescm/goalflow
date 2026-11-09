using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using GoalFlow.Application.Torneos.Contracts;
using GoalFlow.Infrastructure.Mensajeria;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static GoalFlow.Tests.Isolation.BaseDeAislamiento;
using static GoalFlow.Tests.Isolation.EntornoDePlanilla;
using static GoalFlow.Tests.Mensajeria.AyudantesDeMensajeria;

namespace GoalFlow.Tests.Isolation;

/// <summary>
/// Spec 006 T-08, T-21 y T-23: concurrencia gobernada con compuertas (Â§4, C-24). Cada prueba afirma el orden observado por los
/// decoradores de los puertos de almacÃ©n; una barrera alrededor de dos peticiones HTTP no es prueba.
/// </summary>
[Collection(ColeccionDePlanilla.Nombre)]
public partial class ConcurrenciaDePlanillaTests(BaseDeAislamiento db)
{
    private const string Evento = "torneos.partido-guardado";

    [GeneratedRegex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex PatronDeGuid();

    private EntornoDePlanilla Entorno(Interleavado control, string consumidor) => new(db, ajustarAnfitrion: b =>
    {
        control.Registrar()(b);
        b.ConfigureTestServices(s => s.AddConsumidorDeEvento<PartidoGuardado, ConsumidorDePartidoA>(consumidor));
    });

    private static string Consumidor() => $"prueba-concurrencia-{Guid.NewGuid():N}";

    private async Task<int[]> VersionesEntregadas(EntornoDePlanilla e, string p, string consumidor)
    {
        var agregado = Guid.Parse((await e.Texto($"select ({e.FiltroDePartido(p, 1, 2)})::text"))!);
        return [.. (await db.LeerEntregas(consumidor)).Where(f => f.AgregadoId == agregado).Select(f => f.Version).Order()];
    }

    private static async Task<(HttpStatusCode Estado, JsonDocument Cuerpo)> Leer(HttpResponseMessage respuesta) =>
        (respuesta.StatusCode, await Cuerpo(respuesta));

    private static void SinFugas(JsonDocument cuerpo)
    {
        var texto = cuerpo.RootElement.GetRawText();
        Assert.DoesNotMatch(PatronDeGuid(), texto);
        foreach (var prohibido in new[] { "fk_", "ux_", "ak_", "23503", "23505", "constraint", "xmin", "Npgsql" })
            Assert.DoesNotContain(prohibido, texto, StringComparison.OrdinalIgnoreCase);
    }

    private static PlanillaJ ConUnaTarjeta(PlanillaJ baseDeLaCorreccion, int cual) =>
        baseDeLaCorreccion with { Tarjetas = [baseDeLaCorreccion.Tarjetas![cual]] };

    /// <summary>Deja el primer partido en la versiÃ³n 2 (la correcciÃ³n de C-05).</summary>
    private static async Task<string> HastaLaVersion2(EntornoDePlanilla e)
    {
        var p = await e.Preparar();
        var ruta = RutaDelPrimerPartido(p);
        Assert.Equal(HttpStatusCode.OK, (await e.Guardar(ruta, Jugado3a1(p, 0))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await e.Guardar(ruta, Corregido3a1(p, 1))).StatusCode);
        return p;
    }

    private static async Task EsperarLecturas(Interleavado control, PuertoDeInterleavado puerto, int cantidad)
    {
        await Esperar(
            () => control.Eventos.Count(x => x.StartsWith($"lectura-{puerto.Nombre}-", StringComparison.Ordinal)) >= cantidad,
            $"{cantidad} lecturas del puerto {puerto.Nombre}");
    }

    // ---- T-08 (a): dos correcciones distintas que leyeron la misma versiÃ³n ----

    [Fact]
    public async Task T08a_dos_correcciones_distintas_que_leyeron_la_misma_version_dejan_una_ganadora_y_un_409_sin_filas_parciales()
    {
        var control = new Interleavado();
        var consumidor = Consumidor();
        await using var e = Entorno(control, consumidor);
        var p = await HastaLaVersion2(e);
        var ruta = RutaDelPrimerPartido(p);
        var base2 = Corregido3a1(p, 2);
        var desde = control.Eventos.Count;
        control.Hoja.TrasLeer = Interleavado.Compuerta(abierta: false);

        var primera = e.Guardar(ruta, ConUnaTarjeta(base2, 0));
        var segunda = e.Guardar(ruta, ConUnaTarjeta(base2, 1));
        await EsperarLecturas(control, control.Hoja, 4); // 2 de la preparaciÃ³n + 2 de esta prueba

        // El entrelazado que se afirma: las dos lecturas devolvieron y ninguna escritura empezÃ³.
        var observados = control.Eventos.Skip(desde).ToList();
        Assert.Equal(2, observados.Count(x => x.StartsWith("lectura-hoja-", StringComparison.Ordinal)));
        Assert.DoesNotContain(observados, x => x.StartsWith("escritura-hoja-", StringComparison.Ordinal));
        control.Hoja.TrasLeer.SetResult();

        var respuestas = await Task.WhenAll(primera, segunda);
        var resultados = await Task.WhenAll(respuestas.Select(Leer));

        var ganadora = Assert.Single(resultados, r => r.Estado == HttpStatusCode.OK);
        var perdedora = Assert.Single(resultados, r => r.Estado == HttpStatusCode.Conflict);
        Assert.Equal("guardada", ganadora.Cuerpo.RootElement.GetProperty("resultado").GetString());
        Assert.Equal(3, ganadora.Cuerpo.RootElement.GetProperty("version").GetInt32());
        Assert.Equal("conflicto_de_version", perdedora.Cuerpo.RootElement.GetProperty("codigo").GetString());
        SinFugas(perdedora.Cuerpo);
        Assert.DoesNotContain(resultados, r => (int)r.Estado >= 500);

        // Ambas escrituras empezaron despuÃ©s de las dos lecturas (orden observado).
        var tras = control.Eventos.Skip(desde).ToList();
        var primeraEscritura = tras.FindIndex(x => x.StartsWith("escritura-hoja-", StringComparison.Ordinal));
        Assert.True(tras.FindLastIndex(x => x.StartsWith("lectura-hoja-", StringComparison.Ordinal)) < primeraEscritura);

        // Solo las filas de la ganadora: ninguna inserciÃ³n parcial de la perdedora.
        Assert.Equal(3, await e.Planillas(p));
        Assert.Equal(9, await e.Goles(p)); // 3 + 3 + 3
        Assert.Equal(5, await e.Tarjetas(p)); // 2 + 2 + 1
        Assert.Equal(3, await e.VersionDelPartido(p));
        Assert.Equal([1, 2, 3], await VersionesEntregadas(e, p, consumidor)); // una entrega por versiÃ³n
    }

    // ---- T-08 (b): dos correcciones idÃ©nticas que leyeron la misma versiÃ³n ----

    [Fact]
    public async Task T08b_dos_correcciones_identicas_que_leyeron_la_misma_version_dejan_una_ganadora_y_un_409()
    {
        var control = new Interleavado();
        var consumidor = Consumidor();
        await using var e = Entorno(control, consumidor);
        var p = await HastaLaVersion2(e);
        var ruta = RutaDelPrimerPartido(p);
        var identica = ConUnaTarjeta(Corregido3a1(p, 2), 0);
        var desde = control.Eventos.Count;
        control.Hoja.TrasLeer = Interleavado.Compuerta(abierta: false);

        var primera = e.Guardar(ruta, identica);
        var segunda = e.Guardar(ruta, identica);
        await EsperarLecturas(control, control.Hoja, 4);
        Assert.DoesNotContain(control.Eventos.Skip(desde), x => x.StartsWith("escritura-hoja-", StringComparison.Ordinal));
        control.Hoja.TrasLeer.SetResult();

        var resultados = await Task.WhenAll((await Task.WhenAll(primera, segunda)).Select(Leer));

        var ganadora = Assert.Single(resultados, r => r.Estado == HttpStatusCode.OK);
        var perdedora = Assert.Single(resultados, r => r.Estado == HttpStatusCode.Conflict);
        Assert.Equal("guardada", ganadora.Cuerpo.RootElement.GetProperty("resultado").GetString());
        Assert.Equal("conflicto_de_version", perdedora.Cuerpo.RootElement.GetProperty("codigo").GetString());
        SinFugas(perdedora.Cuerpo);
        Assert.Equal(3, await e.Planillas(p));
        Assert.Equal(3, await e.VersionDelPartido(p));
        Assert.Equal([1, 2, 3], await VersionesEntregadas(e, p, consumidor));
    }

    // ---- T-08 (c): la misma correcciÃ³n idÃ©ntica, la segunda lectura despuÃ©s del primer commit ----

    [Fact]
    public async Task T08c_si_la_segunda_lectura_ocurre_despues_del_commit_de_la_primera_es_sinCambios()
    {
        var control = new Interleavado();
        var consumidor = Consumidor();
        await using var e = Entorno(control, consumidor);
        var p = await HastaLaVersion2(e);
        var ruta = RutaDelPrimerPartido(p);
        var identica = ConUnaTarjeta(Corregido3a1(p, 2), 0);
        var desde = control.Eventos.Count;

        // La tercera lectura del puerto (la de la preparaciÃ³n fueron dos) es la de la primera peticiÃ³n que llegue; se
        // retiene la cuarta, es decir, la de la segunda peticiÃ³n, hasta que la primera haya confirmado.
        control.Hoja.RetenerLaLecturaNumero = 4;
        control.Hoja.AntesDeLeer = Interleavado.Compuerta(abierta: false);

        var uno = e.Guardar(ruta, identica);
        var dos = e.Guardar(ruta, identica);
        var primeraEnTerminar = await Task.WhenAny(uno, dos);
        var segundaEnTerminar = primeraEnTerminar == uno ? dos : uno;
        var respuestaDeLaPrimera = await primeraEnTerminar;
        control.Anotar("respuesta-primera");
        Assert.False(segundaEnTerminar.IsCompleted);
        control.Hoja.AntesDeLeer.SetResult();
        var respuestaDeLaSegunda = await segundaEnTerminar;

        var (estadoPrimera, primera) = await Leer(respuestaDeLaPrimera);
        var (estadoSegunda, segunda) = await Leer(respuestaDeLaSegunda);
        Assert.Equal(HttpStatusCode.OK, estadoPrimera);
        Assert.Equal(HttpStatusCode.OK, estadoSegunda);
        Assert.Equal("guardada", primera.RootElement.GetProperty("resultado").GetString());
        Assert.Equal("sinCambios", segunda.RootElement.GetProperty("resultado").GetString());
        Assert.Equal(3, segunda.RootElement.GetProperty("version").GetInt32());

        // Orden observado: lectura 3, escritura 3, respuesta de la primera, y solo entonces la lectura 4.
        var tras = control.Eventos.Skip(desde).ToList();
        Assert.True(tras.IndexOf("lectura-hoja-3") < tras.IndexOf("escritura-hoja-3"));
        Assert.True(tras.IndexOf("escritura-hoja-3") < tras.IndexOf("respuesta-primera"));
        Assert.True(tras.IndexOf("respuesta-primera") < tras.IndexOf("lectura-hoja-4"));
        Assert.DoesNotContain("escritura-hoja-4", tras);

        Assert.Equal(3, await e.Planillas(p));
        Assert.Equal([1, 2, 3], await VersionesEntregadas(e, p, consumidor));
    }

    // ---- T-23 (a): la planilla lee, la importaciÃ³n cancela y confirma, la planilla escribe ----

    [Fact]
    public async Task T23a_si_la_importacion_cancela_el_partido_entre_la_lectura_y_la_escritura_de_la_planilla_la_planilla_recibe_409()
    {
        var control = new Interleavado();
        var consumidor = Consumidor();
        await using var e = Entorno(control, consumidor);
        var p = await e.Preparar();
        var ruta = RutaDelPrimerPartido(p);
        control.Hoja.TrasLeer = Interleavado.Compuerta(abierta: false);

        var hoja = e.Guardar(ruta, Jugado3a1(p, 0));
        await EsperarLecturas(control, control.Hoja, 1);
        var cancelada = Torneo(p) with { Partidos = [.. Torneo(p).Partidos.Select((m, i) => i == 0 ? m with { Cancelado = true } : m)] };
        var importacion = await e.Importar(p, cancelada);
        Assert.Equal(HttpStatusCode.OK, importacion.StatusCode);
        control.Anotar("respuesta-importacion");
        control.Hoja.TrasLeer.SetResult();
        var (estado, cuerpo) = await Leer(await hoja);

        Assert.Equal(HttpStatusCode.Conflict, estado);
        Assert.Equal("conflicto_de_version", cuerpo.RootElement.GetProperty("codigo").GetString());
        SinFugas(cuerpo);

        // Orden observado: la planilla leyÃ³ antes de que la importaciÃ³n leyera, escribiera y respondiera.
        // (la importaciÃ³n de la preparaciÃ³n fue la lectura 1 de su puerto; la que cancela es la 2)
        Assert.True(control.Posicion("lectura-hoja-1") < control.Posicion("lectura-importacion-2"));
        Assert.True(control.Posicion("lectura-importacion-2") < control.Posicion("escritura-importacion-2"));
        Assert.True(control.Posicion("escritura-importacion-2") < control.Posicion("respuesta-importacion"));
        Assert.True(control.Posicion("respuesta-importacion") < control.Posicion("escritura-hoja-1"));

        Assert.Equal("Cancelado", await e.EstadoDelPartido(p));
        Assert.Equal(0, await e.VersionDelPartido(p));
        Assert.Equal(0, await e.Planillas(p));
        Assert.Equal([], await VersionesEntregadas(e, p, consumidor));
    }

    // ---- T-23 (b) y (c): la importaciÃ³n lee, la planilla guarda y confirma, la importaciÃ³n escribe ----

    private async Task ImportacionPerdedora(Func<string, TorneoJ> cambio)
    {
        var control = new Interleavado();
        var consumidor = Consumidor();
        await using var e = Entorno(control, consumidor);
        var p = await e.Preparar();
        var ruta = RutaDelPrimerPartido(p);
        control.Importacion.TrasLeer = Interleavado.Compuerta(abierta: false);

        var importacion = e.Importar(p, cambio(p));
        await EsperarLecturas(control, control.Importacion, 2); // la preparaciÃ³n leyÃ³ una vez
        var guardada = await e.Guardar(ruta, Jugado3a1(p, 0));
        Assert.Equal(HttpStatusCode.OK, guardada.StatusCode);
        control.Anotar("respuesta-hoja");
        control.Importacion.TrasLeer.SetResult();
        var (estado, cuerpo) = await Leer(await importacion);

        Assert.Equal(HttpStatusCode.Conflict, estado);
        var conflicto = Assert.Single(cuerpo.RootElement.GetProperty("conflictos").EnumerateArray());
        Assert.Equal("concurrencia", conflicto.GetProperty("tipo").GetString());
        SinFugas(cuerpo);

        // Orden observado: la importaciÃ³n leyÃ³ antes de que la planilla leyera, escribiera y respondiera.
        Assert.True(control.Posicion("lectura-importacion-2") < control.Posicion("lectura-hoja-1"));
        Assert.True(control.Posicion("escritura-hoja-1") < control.Posicion("respuesta-hoja"));
        Assert.True(control.Posicion("respuesta-hoja") < control.Posicion("escritura-importacion-2"));

        Assert.Equal("Jugado", await e.EstadoDelPartido(p));
        Assert.Equal(1, await e.VersionDelPartido(p));
        Assert.Equal(1, await e.Planillas(p));
        Assert.Equal([1], await VersionesEntregadas(e, p, consumidor));
        Assert.Equal(1, await e.Contar($"select jornada from goalflow.partidos where id = ({e.FiltroDePartido(p, 1, 2)})"));
    }

    [Fact]
    public Task T23b_si_la_planilla_guarda_entre_la_lectura_y_la_escritura_de_la_importacion_que_cancela_la_importacion_recibe_409() =>
        ImportacionPerdedora(p => Torneo(p) with { Partidos = [.. Torneo(p).Partidos.Select((m, i) => i == 0 ? m with { Cancelado = true } : m)] });

    [Fact]
    public Task T23c_lo_mismo_con_un_cambio_de_jornada_en_lugar_de_una_cancelacion() =>
        ImportacionPerdedora(p => Torneo(p) with { Partidos = [.. Torneo(p).Partidos.Select((m, i) => i == 0 ? m with { Jornada = 5 } : m)] });

    // ---- T-21 (R-07) ----

    [Fact]
    public async Task T21_tras_guardar_corregir_retirar_adjudicar_e_importar_el_estado_del_partido_es_el_de_su_planilla_vigente()
    {
        await using var e = new EntornoDePlanilla(db);
        var p = await e.Preparar();
        var ruta = RutaDelPrimerPartido(p);

        await e.Guardar(ruta, Jugado3a1(p, 0));
        await e.ExigirEstadoConsistente(p);
        await e.Guardar(ruta, Corregido3a1(p, 1));
        await e.Guardar(ruta, Jugado3a1(p, 2) with { Estado = "Adjudicado", Oficial = new(3, 0), Motivo = "Sancion", MotivoRectificacion = "DecisionReglamentaria" });
        await e.Guardar(ruta, Retirada(3));
        await e.ExigirEstadoConsistente(p);
        Assert.Equal("Programado", await e.EstadoDelPartido(p));
        Assert.Equal(4, await e.VersionDelPartido(p));

        var cancelada = Torneo(p) with { Partidos = [.. Torneo(p).Partidos.Select((m, i) => i == 0 ? m with { Cancelado = true } : m)] };
        Assert.Equal(HttpStatusCode.OK, (await e.Importar(p, cancelada)).StatusCode);
        Assert.Equal("Cancelado", await e.EstadoDelPartido(p));
        Assert.Equal(4, await e.VersionDelPartido(p)); // la importaciÃ³n nunca cambia la versiÃ³n (R-30)
        await e.ExigirEstadoConsistente(p);
    }

    [Fact]
    public async Task T21_canario_la_comprobacion_detecta_un_estado_que_diverge_de_su_planilla()
    {
        await using var e = new EntornoDePlanilla(db);
        var p = await e.Preparar();
        await using var conexion = new NpgsqlConnection(db.CadenaSuperusuario);
        await conexion.OpenAsync();
        await using var transaccion = await conexion.BeginTransactionAsync();

        await using (var forzar = new NpgsqlCommand(
            $"update goalflow.partidos set estado = 'Jugado' where id = ({e.FiltroDePartido(p, 1, 2)})", conexion, transaccion))
            await forzar.ExecuteNonQueryAsync();
        await using var medir = new NpgsqlCommand(PartidosDivergentes(p), conexion, transaccion);
        var divergentes = (long)(await medir.ExecuteScalarAsync())!;
        await transaccion.RollbackAsync();

        Assert.Equal(1, divergentes);
    }
}
