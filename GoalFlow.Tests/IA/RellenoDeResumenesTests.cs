using GoalFlow.Application.IA;
using GoalFlow.Application.Mensajeria;
using GoalFlow.Application.Torneos.Contracts;
using GoalFlow.Infrastructure.IA;
using GoalFlow.Infrastructure.Mensajeria;
using GoalFlow.Tests.Isolation;
using GoalFlow.Tests.Mensajeria;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using static GoalFlow.Tests.Isolation.BaseDeAislamiento;

namespace GoalFlow.Tests.IA;

/// <summary>
/// Colección propia con su propia base: el backfill recorre todas las ligas, así que cada prueba parte de una base sin partidos
/// (<c>LimpiarAsync</c>) y los conteos son exactos.
/// </summary>
[CollectionDefinition(Nombre, DisableParallelization = true)]
public class ColeccionDeBackfill : ICollectionFixture<BaseDeAislamiento>
{
    public const string Nombre = "IA-Backfill";
}

/// <summary>Decora el publicador del host de prueba: tras escribir la fila espera en una barrera a que llegue el otro trabajador (Spec 007 T-27 c).</summary>
public sealed class PublicadorConBarrera(IPublicadorDeEventos interno, CountdownEvent barrera, List<string> eventos) : IPublicadorDeEventos
{
    public void Publicar<TEvento>(TEvento evento) where TEvento : IEventoDeIntegracion => interno.Publicar(evento);

    public void PublicarPara<TEvento>(TEvento evento, string consumidor) where TEvento : IEventoDeIntegracion
    {
        interno.PublicarPara(evento, consumidor);
        lock (eventos) eventos.Add("publicado");
        barrera.Signal();
        Assert.True(barrera.Wait(TimeSpan.FromSeconds(20)), "El otro trabajador no llegó a publicar");
    }
}

/// <summary>Spec 007 T-27 (R-32, R-34) y T-28 (R-07, backfill): la publicación dirigida de los partidos con resultado.</summary>
[Collection(ColeccionDeBackfill.Nombre)]
public class RellenoDeResumenesTests(BaseDeAislamiento db)
{
    private const string A01 = ConsumidorDeResumenDePartido.NombreDelConsumidor;
    private const string OtroConsumidor = "prueba-otro-consumidor";

    /// <summary>Deja la base sin partidos ni entregas: el backfill recorre todas las ligas.</summary>
    private async Task LimpiarAsync() =>
        await db.EjecutarComoSuperusuario("delete from goalflow.entregas_eventos; truncate goalflow.partidos cascade;");

    private static Action<IServiceCollection> ConOtroConsumidor => s =>
        s.AddConsumidorDeEvento<PartidoGuardado, ConsumidorDePartidoA>(OtroConsumidor);

    private static BackfillDeResumenes Relleno(AnfitrionDeIa host) => host.Services.GetRequiredService<BackfillDeResumenes>();

    private static async Task<PartidoSembrado> Con(BaseDeAislamiento db, GoalFlow.Domain.Tenancy.TenantId liga, PlanillaSembrada planilla)
    {
        var p = await PartidoSembrado.CrearAsync(db, liga);
        await p.GuardarAsync(planilla);
        return p;
    }

    private static Task<PartidoSembrado> Jugado(BaseDeAislamiento db, GoalFlow.Domain.Tenancy.TenantId liga) =>
        Con(db, liga, PlanillaSembrada.ConGoleador());

    private Task<List<object?[]>> Entregas(AnfitrionDeIa host, string consumidor) => host.Filas($"""
        select tenant_id, agregado_id, version_del_agregado, estado from goalflow.entregas_eventos
        where consumidor = '{consumidor}' order by agregado_id
        """);

    // ---- T-27 (a): cobertura y aislamiento ----

    [Fact]
    public async Task T27a_crea_una_fila_de_A01_por_partido_con_resultado_de_cada_liga_y_ninguna_por_un_Suspendido()
    {
        await LimpiarAsync();
        await using var host = AnfitrionDeIa.Construir(db);
        var a1 = await Jugado(db, LigaA);
        var a2 = await Con(db, LigaA, new("Adjudicado", Oficial: (3, 0), Motivo: "WalkOver", TarjetasInformadas: null));
        var a3 = await Con(db, LigaA, new("Suspendido", Motivo: "Clima"));
        var b1 = await Jugado(db, LigaB);

        var resultado = await Relleno(host).EjecutarAsync(default);

        Assert.Equal((3, 0, 0), (resultado.Creadas, resultado.YaPresentes, resultado.Fallidas));
        var filas = await Entregas(host, A01);
        Assert.Equal(3, filas.Count);
        Assert.Equal(
            new[] { (LigaA.Value, a1.Id), (LigaA.Value, a2.Id), (LigaB.Value, b1.Id) }.Order().ToArray(),
            filas.Select(f => ((Guid)f[0]!, (Guid)f[1]!)).Order().ToArray()); // cada fila en su tenant
        Assert.DoesNotContain(filas, f => (Guid)f[1]! == a3.Id);
        Assert.All(filas, f => Assert.Equal((1, "Pendiente"), ((int)f[2]!, (string)f[3]!)));
    }

    [Fact]
    public async Task T27a_publica_la_version_vigente_del_partido_no_la_primera()
    {
        await LimpiarAsync();
        await using var host = AnfitrionDeIa.Construir(db);
        var p = await Jugado(db, LigaA);
        await p.GuardarAsync(PlanillaSembrada.ConGoleador("Jugador Dos"));
        await p.GuardarAsync(PlanillaSembrada.ConGoleador("Jugador Uno", informadas: false)); // v3

        await Relleno(host).EjecutarAsync(default);

        var fila = Assert.Single(await Entregas(host, A01));
        Assert.Equal(3, (int)fila[2]!);
    }

    // ---- T-27 (b): en secuencia ----

    [Fact]
    public async Task T27b_correrlo_dos_veces_la_segunda_crea_cero_y_cuenta_tres_presentes()
    {
        await LimpiarAsync();
        await using var host = AnfitrionDeIa.Construir(db);
        await Jugado(db, LigaA);
        await Con(db, LigaA, new("Adjudicado", Oficial: (3, 0), Motivo: "Sancion", TarjetasInformadas: null));
        await Jugado(db, LigaB);

        var primera = await Relleno(host).EjecutarAsync(default);
        var segunda = await Relleno(host).EjecutarAsync(default);

        Assert.Equal((3, 0), (primera.Creadas, primera.YaPresentes));
        Assert.Equal((0, 3, 0), (segunda.Creadas, segunda.YaPresentes, segunda.Fallidas));
        Assert.Equal(3, (await Entregas(host, A01)).Count);
    }

    // ---- T-27 (c), crítica: dos corridas sobre el mismo partido, las dos pasan la publicación antes de que una confirme ----

    [Fact]
    public async Task T27c_dos_corridas_concurrentes_sobre_el_mismo_partido_dejan_una_sola_fila_y_ninguna_falla()
    {
        await LimpiarAsync();
        var eventos = new List<string>();
        var barrera = new CountdownEvent(2);
        await using var host = AnfitrionDeIa.Construir(db, servicios: s =>
        {
            s.RemoveAll<IPublicadorDeEventos>();
            s.AddScoped<IPublicadorDeEventos>(sp =>
                new PublicadorConBarrera(ActivatorUtilities.CreateInstance<PublicadorDeEventos>(sp), barrera, eventos));
        });
        var p = await Jugado(db, LigaA);

        var corridas = await Task.WhenAll(
            Task.Run(() => Relleno(host).EjecutarAsync(default)),
            Task.Run(() => Relleno(host).EjecutarAsync(default)));

        Assert.Equal(["publicado", "publicado"], eventos); // las dos publicaron antes de que cualquiera confirmara
        Assert.Equal(1, corridas.Sum(c => c.Creadas));
        Assert.Equal(1, corridas.Sum(c => c.YaPresentes)); // la perdedora cuenta el partido como ya presente
        Assert.All(corridas, c => Assert.Equal(0, c.Fallidas));
        var fila = Assert.Single(await Entregas(host, A01));
        Assert.Equal(p.Id, (Guid)fila[1]!);
    }

    // ---- T-27 (d): otros consumidores del mismo evento ----

    [Fact]
    public async Task T27d1_si_la_fila_del_otro_consumidor_existe_y_la_de_A01_no_crea_exactamente_la_de_A01()
    {
        await LimpiarAsync();
        await using var host = AnfitrionDeIa.Construir(db, servicios: ConOtroConsumidor);
        var p = await Jugado(db, LigaA);
        await db.EjecutarComoSuperusuario($"""
            insert into goalflow.entregas_eventos (id, tenant_id, evento_id, tipo_de_evento, agregado_id, version_del_agregado, consumidor, estado, intentos)
            values ('{Guid.NewGuid()}', '{LigaA.Value}', '{Guid.NewGuid()}', 'torneos.partido-guardado', '{p.Id}', 1, '{OtroConsumidor}', 'Pendiente', 0)
            """);

        var resultado = await Relleno(host).EjecutarAsync(default);

        Assert.Equal((1, 0, 0), (resultado.Creadas, resultado.YaPresentes, resultado.Fallidas)); // con Publicar daría 0 y un duplicado
        Assert.Single(await Entregas(host, A01));
        Assert.Single(await Entregas(host, OtroConsumidor)); // la del otro consumidor quedó como estaba
    }

    [Fact]
    public async Task T27d2_si_ninguna_existe_crea_solo_la_de_A01_y_el_otro_consumidor_sigue_sin_fila()
    {
        await LimpiarAsync();
        await using var host = AnfitrionDeIa.Construir(db, servicios: ConOtroConsumidor);
        await Jugado(db, LigaA);

        var resultado = await Relleno(host).EjecutarAsync(default);

        Assert.Equal(1, resultado.Creadas);
        Assert.Single(await Entregas(host, A01));
        Assert.Empty(await Entregas(host, OtroConsumidor)); // con Publicar tendría una fila
    }

    // ---- R-32: una vez por arranque, solo con la IA y la bandera, y nunca tumba el host ----

    [Fact]
    public async Task T28_con_la_IA_apagada_el_backfill_no_corre_aunque_su_bandera_este_encendida()
    {
        await LimpiarAsync();
        await using var host = AnfitrionDeIa.Construir(db, c =>
        {
            c["IA:Habilitada"] = "false";
            c["IA:ClaveApi"] = null;
            c["IA:Backfill:EjecutarAlArrancar"] = "true";
        }, conGenerador: false);
        await Jugado(db, LigaA);

        await host.IniciarAsync();
        await Task.Delay(TimeSpan.FromSeconds(2));

        Assert.Empty(await Entregas(host, A01));
        Assert.Empty(host.Services.GetServices<IHostedService>().OfType<RellenoAlArrancar>());
        Assert.Null(host.Services.GetService<BackfillDeResumenes>());
    }

    [Fact]
    public async Task T28_con_la_IA_y_la_bandera_corre_una_vez_por_arranque_y_un_segundo_arranque_no_duplica()
    {
        await LimpiarAsync();
        var registros = new CapturaDeRegistros();
        await using (var primero = AnfitrionDeIa.Construir(db, c => c["IA:Backfill:EjecutarAlArrancar"] = "true", registros: registros))
        {
            await Jugado(db, LigaA);
            await primero.IniciarAsync();
            await Esperar(async () => (await Entregas(primero, A01)).Count == 1);
            await EsperarElFinDelBackfillAsync(primero);
            Assert.Contains(registros.Entradas, e => e.Nivel == LogLevel.Information && e.Texto.Contains("creadas 1") && e.Texto.Contains("ya presentes 0"));
        }

        await using var segundo = AnfitrionDeIa.Construir(db, c => c["IA:Backfill:EjecutarAlArrancar"] = "true", registros: registros);
        await segundo.IniciarAsync();
        await Esperar(() => Task.FromResult(registros.Entradas.Count(e => e.Texto.Contains("creadas 0") && e.Texto.Contains("ya presentes 1")) >= 1));

        await EsperarElFinDelBackfillAsync(segundo);
        Assert.Single(await Entregas(segundo, A01));
    }

    [Fact]
    public async Task T28_con_la_IA_encendida_pero_sin_la_bandera_el_backfill_no_corre()
    {
        await LimpiarAsync();
        await using var host = AnfitrionDeIa.Construir(db);
        await Jugado(db, LigaA);

        await host.IniciarAsync();
        await Task.Delay(TimeSpan.FromSeconds(2));

        Assert.Empty(await Entregas(host, A01));
        Assert.Empty(host.Services.GetServices<IHostedService>().OfType<RellenoAlArrancar>());
    }

    [Fact]
    public async Task R32_una_excepcion_en_el_backfill_se_registra_como_tipo_y_no_tumba_el_host()
    {
        await LimpiarAsync();
        var registros = new CapturaDeRegistros();
        await using var host = AnfitrionDeIa.Construir(db, c => c["IA:Backfill:EjecutarAlArrancar"] = "true", registros: registros,
            servicios: s =>
            {
                s.RemoveAll<IListadoDePartidosConResultado>();
                s.AddScoped<IListadoDePartidosConResultado, ListadoQueFalla>();
            });
        await Jugado(db, LigaA);

        await host.IniciarAsync(); // el host arranca
        await Esperar(() => Task.FromResult(registros.Entradas.Any(e => e.Nivel == LogLevel.Warning && e.Texto.Contains("InvalidOperationException"))));

        Assert.DoesNotContain(registros.Entradas, e => e.Texto.Contains("MENSAJE-QUE-NO-SE-REGISTRA"));
        Assert.Empty(await Entregas(host, A01));
    }

    private sealed class ListadoQueFalla : IListadoDePartidosConResultado
    {
        public Task<IReadOnlyList<PartidoConResultado>> ListarAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("MENSAJE-QUE-NO-SE-REGISTRA");
    }

    /// <summary>El backfill escribe su línea de conteos después de crear la fila: se espera al servicio antes de leer el registro.</summary>
    private static Task EsperarElFinDelBackfillAsync(AnfitrionDeIa host) =>
        host.Services.GetServices<IHostedService>().OfType<RellenoAlArrancar>().Single().ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(30));

    private static async Task Esperar(Func<Task<bool>> condicion)
    {
        var fin = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < fin)
        {
            if (await condicion()) return;
            await Task.Delay(100);
        }

        Assert.Fail("No se cumplió la condición a tiempo");
    }

    // ---- R-34: el listado ----

    [Fact]
    public async Task R34_el_listado_trae_solo_Jugado_y_Adjudicado_con_version_vigente_ordenados_por_id()
    {
        await LimpiarAsync();
        await using var host = AnfitrionDeIa.Construir(db);
        var jugado = await Jugado(db, LigaA);
        var adjudicado = await Con(db, LigaA, new("Adjudicado", Oficial: (3, 0), Motivo: "WalkOver", TarjetasInformadas: null));
        await Con(db, LigaA, new("Suspendido", Motivo: "Clima"));
        await PartidoSembrado.CrearAsync(db, LigaA); // Programado, versión 0
        var corregido = await Jugado(db, LigaA);
        await corregido.GuardarAsync(PlanillaSembrada.ConGoleador("Jugador Dos")); // v2

        await using var scope = host.ScopeDe(LigaA);
        await using var transaccion = await scope.ServiceProvider.GetRequiredService<GoalFlow.Application.Persistencia.IUnidadDeTrabajo>().IniciarAsync(default);
        var lista = await scope.ServiceProvider.GetRequiredService<IListadoDePartidosConResultado>().ListarAsync(default);

        Assert.Equal(
            new[] { (jugado.Id, 1), (adjudicado.Id, 1), (corregido.Id, 2) }.OrderBy(x => x.Item1).ToArray(),
            lista.Select(l => (l.PartidoId, l.Version)).ToArray());
    }
}
