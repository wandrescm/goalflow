using GoalFlow.Application.IA;
using GoalFlow.Application.Persistencia;
using GoalFlow.Tests.Isolation;
using Microsoft.Extensions.DependencyInjection;
using static GoalFlow.Tests.Isolation.BaseDeAislamiento;

namespace GoalFlow.Tests.IA;

/// <summary>
/// Spec 007 T-10, T-11, T-12, T-16 y T-17: concurrencia gobernada con compuertas (§4, patrón de la Spec 006 C-24). Cada prueba afirma
/// el orden que observaron los decoradores de los puertos; dos peticiones enviadas juntas no son prueba. Los trabajadores usan
/// conexiones distintas a PostgreSQL 17 como goalflow_app, así que los bloqueos y el compare-and-swap son los reales.
/// </summary>
[Collection(ColeccionDeIa.Nombre)]
public class ConcurrenciaDeIaTests(BaseDeAislamiento db)
{
    private static readonly TimeSpan Limite = TimeSpan.FromSeconds(20);

    private static async Task EsperarAsync(Func<Task<bool>> condicion, string que)
    {
        var fin = DateTime.UtcNow + Limite;
        while (DateTime.UtcNow < fin)
        {
            if (await condicion()) return;
            await Task.Delay(50);
        }

        Assert.Fail($"No se alcanzó a tiempo: {que}");
    }

    private static Task EsperarEventoAsync(ControlDeIa control, string evento) =>
        EsperarAsync(() => Task.FromResult(control.Eventos.Contains(evento)), $"el evento '{evento}'. Observados: {string.Join(", ", control.Eventos)}");

    /// <summary>Hay una sesión de goalflow_app esperando un bloqueo: el segundo trabajador ya leyó y quedó detrás del primero.</summary>
    private Task EsperarUnBloqueoAsync(AnfitrionDeIa host) => EsperarAsync(
        async () => await host.Contar(
            "select count(*) from pg_stat_activity where datname = current_database() and usename = 'goalflow_app' and wait_event_type = 'Lock'") >= 1,
        "una sesión de goalflow_app esperando un bloqueo");

    private static Action<Dictionary<string, string?>> SinMargen => c =>
    {
        c["IA:MargenDeEntradaPorcentaje"] = "0";
        c["IA:MargenDeEntradaTokens"] = "0";
    };

    private async Task<(string Resultado, Exception? Error)> Correr(Func<Task> trabajo)
    {
        try
        {
            await trabajo();
            return ("ok", null);
        }
        catch (Exception ex)
        {
            return ("error", ex);
        }
    }

    // ---- T-10 (R-19, crítica): dos reservas de 700 con 1.000 disponibles ----

    [Fact]
    public async Task T10_dos_reservas_de_700_con_1000_disponibles_solo_una_prospera_y_la_otra_guarda_un_borrador_de_presupuesto()
    {
        var control = new ControlDeIa { LlegadasEsperadasParaReservar = 2 };
        await using var host = AnfitrionDeIa.Construir(db, SinMargen, control.Registrar());
        host.Control.TokensContados = _ => 4_000; // sin márgenes: ⌈4000 × 0,1⌉ + 300 = 700 µ$
        host.Control.AlGenerar = _ => throw new ResultadoInciertoException(); // la reserva ganadora queda contada
        var p1 = await PartidoSembrado.CrearAsync(db, LigaA);
        var p2 = await PartidoSembrado.CrearAsync(db, LigaA);
        await p1.GuardarAsync(PlanillaSembrada.ConGoleador("Jugador Uno"));
        await p2.GuardarAsync(PlanillaSembrada.ConGoleador("Jugador Dos"));
        await db.EjecutarComoSuperusuario($"""
            insert into goalflow.presupuestos_ia (id, tenant_id, mes, tope, reservado, consumido)
            values ('{Guid.NewGuid()}', '{LigaA.Value}', '{host.MesActual}', 1000, 0, 0)
            """);

        var primero = Correr(() => host.ProcesarAsync(p1, 1));
        var segundo = Correr(() => host.ProcesarAsync(p2, 1));
        await EsperarEventoAsync(control, "reservar-llego");
        await Task.WhenAll(primero, segundo);

        // El orden observado: los dos terminaron de leer el contador antes de que cualquiera reservara.
        var eventos = control.Eventos;
        Assert.Equal(2, eventos.Count(e => e == "asegurar-fin-1" || e == "asegurar-fin-2"));
        Assert.Equal(2, eventos.Count(e => e == "reservar-llego"));
        var primerInicio = eventos.ToList().IndexOf("reservar-inicio");
        Assert.True(primerInicio > eventos.ToList().IndexOf("asegurar-fin-1"));
        Assert.True(primerInicio > eventos.ToList().IndexOf("asegurar-fin-2"));
        Assert.Equal(1, eventos.Count(e => e == "reservar-fin-exito"));
        Assert.Equal(1, eventos.Count(e => e == "reservar-fin-sin-saldo"));

        // Exactamente una reservó; la otra guardó el borrador de presupuesto.
        var (r1, r2) = (await primero, await segundo);
        var ganadora = new[] { r1, r2 }.Single(r => r.Resultado == "error");
        Assert.IsType<FalloReintentableException>(ganadora.Error); // su llamada fue incierta
        Assert.Equal(1, await host.Llamadas(p1) + await host.Llamadas(p2));
        Assert.Equal((700L, 0L), await host.Contador(LigaA, host.MesActual));
        var borradores = (await host.ResumenesDe(p1)).Concat(await host.ResumenesDe(p2)).ToList();
        var borrador = Assert.Single(borradores);
        Assert.Equal(("Plantilla", "Borrador", "presupuesto"), (borrador.Origen, borrador.Estado, borrador.Motivo));
        Assert.Equal(1, host.Control.Generaciones);
    }

    // ---- T-11 (R-19 a): dos primeras reservas del mes crean el contador a la vez ----

    [Fact]
    public async Task T11_dos_primeras_reservas_del_mes_crean_un_solo_contador_y_ninguna_falla()
    {
        var control = new ControlDeIa { TrasAsegurarElPrimero = ControlDeIa.Compuerta() };
        await using var host = AnfitrionDeIa.Construir(db, servicios: control.Registrar());
        var p1 = await PartidoSembrado.CrearAsync(db, LigaA);
        var p2 = await PartidoSembrado.CrearAsync(db, LigaA);
        await p1.GuardarAsync(PlanillaSembrada.ConGoleador("Jugador Uno"));
        await p2.GuardarAsync(PlanillaSembrada.ConGoleador("Jugador Dos"));
        Assert.Equal(0, await host.Contadores(LigaA, host.MesActual));

        // El primero inserta el contador y queda retenido con su transacción abierta.
        var primero = Correr(() => host.ProcesarAsync(p1, 1));
        await EsperarEventoAsync(control, "asegurar-fin-1");

        // El segundo leyó que no existe (la fila del primero no está confirmada) e intenta insertar: queda detrás del primero.
        var segundo = Correr(() => host.ProcesarAsync(p2, 1));
        await EsperarEventoAsync(control, "asegurar-inicio-2");
        await EsperarUnBloqueoAsync(host);
        Assert.DoesNotContain("asegurar-fin-2", control.Eventos); // sigue esperando el índice único de (tenant, mes)
        control.TrasAsegurarElPrimero.SetResult();

        var resultados = await Task.WhenAll(primero, segundo);

        Assert.All(resultados, r => Assert.True(r.Resultado == "ok", r.Error?.GetType().Name)); // ni 500 ni excepción
        Assert.Equal(1, await host.Contadores(LigaA, host.MesActual));
        Assert.Contains("asegurar-fin-2", control.Eventos); // el segundo usó la fila existente
        Assert.Equal(2, control.Eventos.Count(e => e == "reservar-fin-exito")); // las dos se evaluaron contra ese contador
        Assert.Equal((0L, 800L), await host.Contador(LigaA, host.MesActual)); // dos llamadas de 400 µ$ conciliadas
        Assert.Equal(1, await host.Resumenes(p1));
        Assert.Equal(1, await host.Resumenes(p2));
    }

    // ---- T-12 (R-19 c): la misma huella reservada por dos trabajadores ----

    [Fact]
    public async Task T12_la_misma_huella_en_dos_entregas_deja_una_sola_llamada_numero_1_y_la_perdedora_revierte_el_contador()
    {
        var control = new ControlDeIa { TrasInsertarLaPrimeraLlamada = ControlDeIa.Compuerta() };
        await using var host = AnfitrionDeIa.Construir(db, servicios: control.Registrar());
        host.Control.AlGenerar = _ => throw new ResultadoInciertoException();
        var p = await PartidoSembrado.CrearAsync(db, LigaA);
        await p.GuardarAsync(PlanillaSembrada.ConGoleador()); // v1 (se salta: solo la v3 y la v4 importan)
        await db.EjecutarComoSuperusuario($"""
            insert into goalflow.presupuestos_ia (id, tenant_id, mes, tope, reservado, consumido)
            values ('{Guid.NewGuid()}', '{LigaA.Value}', '{host.MesActual}', 1500000, 0, 0)
            """); // el contador ya existe: el choque que se prueba es el del número de llamada, no el del contador
        await p.GuardarAsync(PlanillaSembrada.ConGoleador("Jugador Dos"));
        await p.GuardarAsync(PlanillaSembrada.ConGoleador("Jugador Uno")); // v3

        // El trabajador de la v3 lee el partido en la v3 y reserva; queda retenido con su fila de llamada sin confirmar.
        var deLaV3 = Correr(() => host.ProcesarAsync(p, 3));
        await EsperarEventoAsync(control, "llamada-insertada-1-numero-1");

        // La v4 se guarda con la misma entrada (el solapamiento de un despliegue). Su trabajador cuenta 0 llamadas confirmadas,
        // llega al contador (bloqueado por la primera reserva) y espera.
        await p.GuardarAsync(PlanillaSembrada.ConGoleador("Jugador Uno")); // v4: misma entrada que la v3
        var deLaV4 = Correr(() => host.ProcesarAsync(p, 4));
        await EsperarUnBloqueoAsync(host);
        control.TrasInsertarLaPrimeraLlamada.SetResult();

        var (r3, r4) = (await deLaV3, await deLaV4);

        // Una sola fila de llamada con número 1; la perdedora falló como reintentable y su reserva no quedó contada.
        var llamadas = await host.LlamadasDe(p);
        var llamada = Assert.Single(llamadas);
        Assert.Equal((1, "Reservada"), (llamada.Numero, llamada.Estado));
        Assert.Equal((525L, 0L), await host.Contador(LigaA, host.MesActual));
        Assert.IsType<FalloReintentableException>(r3.Error); // la ganadora: su llamada fue incierta
        Assert.IsType<FalloReintentableException>(r4.Error); // la perdedora: chocó con el número 1 y se revirtió
        Assert.Equal(2, host.Control.Conteos);
        Assert.Equal(1, host.Control.Generaciones); // solo la ganadora llegó al proveedor
        Assert.DoesNotContain(control.Eventos, e => e.StartsWith("llamada-insertada-2", StringComparison.Ordinal)); // la perdedora nunca insertó
    }

    // ---- T-16 (R-22): el resumen del mismo huella choca y la conciliación igual se confirma ----

    [Fact]
    public async Task T16_si_el_resumen_del_segundo_trabajador_choca_su_conciliacion_se_confirma_y_ambas_entregas_completan()
    {
        var control = new ControlDeIa { TrasInsertarElPrimerResumenDelModelo = ControlDeIa.Compuerta() };
        await using var host = AnfitrionDeIa.Construir(db, servicios: control.Registrar());
        var p = await PartidoSembrado.CrearAsync(db, LigaA);
        await p.GuardarAsync(PlanillaSembrada.ConGoleador());

        // A concilia e inserta su resumen y queda retenido sin confirmar (con el contador bloqueado por su conciliación).
        var a = Correr(() => host.ProcesarAsync(p, 1, intento: 1));
        await EsperarEventoAsync(control, "resumen-del-modelo-1-insertado");

        // B leyó que no hay resumen (el de A no está confirmado), reserva la llamada número 2 y espera detrás de A.
        var b = Correr(() => host.ProcesarAsync(p, 1, intento: 2));
        await EsperarUnBloqueoAsync(host);
        control.TrasInsertarElPrimerResumenDelModelo.SetResult();

        var resultados = await Task.WhenAll(a, b);

        Assert.All(resultados, r => Assert.True(r.Resultado == "ok", r.Error?.GetType().Name)); // el duplicado no es un fallo
        Assert.Contains("resumen-del-modelo-2-duplicado", control.Eventos);
        Assert.Equal(1, await host.Resumenes(p));
        var llamadas = await host.LlamadasDe(p);
        Assert.Equal([1, 2], llamadas.Select(l => l.Numero));
        Assert.All(llamadas, l => Assert.Equal("Conciliada", l.Estado)); // la conciliación de B se confirmó
        Assert.Equal((0L, 800L), await host.Contador(LigaA, host.MesActual));
    }

    // ---- T-17 (R-23): conciliar dos veces actualiza el contador una vez ----

    [Fact]
    public async Task T17_conciliar_la_misma_llamada_dos_veces_actualiza_el_contador_una_sola_vez()
    {
        await using var host = AnfitrionDeIa.Construir(db);
        var p = await PartidoSembrado.CrearAsync(db, LigaA);
        await p.GuardarAsync(PlanillaSembrada.ConGoleador());
        var (contador, llamada) = (Guid.NewGuid(), Guid.NewGuid());
        await db.EjecutarComoSuperusuario($"""
            insert into goalflow.presupuestos_ia (id, tenant_id, mes, tope, reservado, consumido)
            values ('{contador}', '{LigaA.Value}', '{host.MesActual}', 1500000, 525, 0);
            insert into goalflow.llamadas_ia (id, tenant_id, torneo_id, partido_id, version, huella, numero, intento_entrega, mes, reserva, estado)
            values ('{llamada}', '{LigaA.Value}', '{p.TorneoId}', '{p.Id}', 1, '{new string('e', 64)}', 1, 1, '{host.MesActual}', 525, 'Reservada');
            """);

        await using var scope = host.ScopeDe(LigaA);
        var almacen = scope.ServiceProvider.GetRequiredService<IAlmacenDeLlamadas>();
        await using var transaccion = await scope.ServiceProvider.GetRequiredService<IUnidadDeTrabajo>().IniciarAsync(default);

        var primera = await almacen.ConciliarAsync(llamada, 2_000, 400, 400, "exito", default);
        var segunda = await almacen.ConciliarAsync(llamada, 2_000, 400, 400, "exito", default);
        await transaccion.ConfirmarAsync(default);

        Assert.True(primera);
        Assert.False(segunda); // no estaba Reservada: no toca el contador
        Assert.Equal((0L, 400L), await host.Contador(LigaA, host.MesActual)); // 525 − 525 reservado y +400 consumido, una vez
    }

    [Fact]
    public async Task T17_conciliar_sin_transaccion_lanza_TransaccionRequerida_y_conciliar_una_llamada_ajena_no_hace_nada()
    {
        await using var host = AnfitrionDeIa.Construir(db);
        await using var scope = host.ScopeDe(LigaA);
        var almacen = scope.ServiceProvider.GetRequiredService<IAlmacenDeLlamadas>();

        await Assert.ThrowsAsync<TransaccionRequeridaException>(() => almacen.ConciliarAsync(Guid.NewGuid(), 1, 1, 1, "exito", default));

        await using var transaccion = await scope.ServiceProvider.GetRequiredService<IUnidadDeTrabajo>().IniciarAsync(default);
        Assert.False(await almacen.ConciliarAsync(Guid.NewGuid(), 1, 1, 1, "exito", default));
    }
}
