using System.Data.Common;
using System.Net;
using System.Text.Json;
using GoalFlow.Application.Persistencia;
using GoalFlow.Application.Tenancy;
using GoalFlow.Application.Torneos.Contracts;
using GoalFlow.Infrastructure.Persistencia;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using static GoalFlow.Tests.Isolation.BaseDeAislamiento;
using static GoalFlow.Tests.Isolation.EntornoDePlanilla;

namespace GoalFlow.Tests.Isolation;

/// <summary>
/// Compuerta del anfitrión de pruebas (Spec 009 T-16, Spec 006 C-24): retiene la lectura marcada después del primer comando
/// del puerto. Solo actúa sobre el flujo asíncrono que la activó (<see cref="AsyncLocal{T}"/>): ninguna otra petición se detiene.
/// Solo existe en el anfitrión de pruebas.
/// </summary>
public sealed class CompuertaDeLecturaConsistente : DbCommandInterceptor
{
    private static readonly AsyncLocal<bool> Activa = new();
    private readonly List<string> _eventos = [];
    private readonly TaskCompletionSource _retenida = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _liberada = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _retenciones;

    /// <summary>Marca el flujo actual: su primera lectura de comando se retendrá hasta <see cref="Liberar"/>.</summary>
    public void Activar() => Activa.Value = true;

    public Task Retenida => _retenida.Task;

    public void Liberar() => _liberada.TrySetResult();

    public IReadOnlyList<string> Eventos
    {
        get
        {
            lock (_eventos) return [.. _eventos];
        }
    }

    public void Anotar(string evento)
    {
        lock (_eventos) _eventos.Add(evento);
    }

    public override async ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    {
        // Solo los comandos del puerto, que corren dentro de la transacción de lectura: la resolución del slug corre sin ella.
        if (Activa.Value && command.Transaction is not null && Interlocked.Increment(ref _retenciones) == 1)
        {
            Anotar("puerto-retenido");
            _retenida.TrySetResult();
            await _liberada.Task.WaitAsync(TimeSpan.FromSeconds(60), cancellationToken);
            Anotar("puerto-liberado");
        }

        return result;
    }
}

/// <summary>
/// Spec 009 Fase A commit 4 (R-11, R-30, R-33): el puerto de lectura de Torneos para Estadísticas y la transacción
/// <c>REPEATABLE READ</c>, por el anfitrión real contra PostgreSQL. Datos inventados.
/// </summary>
[Collection(ColeccionDePlanilla.Nombre)]
public class LectorDeTorneoParaEstadisticasTests(BaseDeAislamiento db)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static async Task<DatosDeTorneoParaEstadisticas?> Leer(IServiceProvider servicios, string torneo)
    {
        await using var scope = servicios.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().Establecer(LigaA);
        await using var transaccion = await scope.ServiceProvider.GetRequiredService<IUnidadDeTrabajo>()
            .IniciarLecturaConsistenteAsync(CancellationToken.None);
        var datos = await scope.ServiceProvider.GetRequiredService<ILectorDeTorneoParaEstadisticas>()
            .LeerAsync(torneo, CancellationToken.None);
        await transaccion.ConfirmarAsync(CancellationToken.None);
        return datos;
    }

    private static PartidoParaEstadisticas Partido(DatosDeTorneoParaEstadisticas datos, string p, int local, int visitante) =>
        Assert.Single(datos.Partidos, x => x.LocalCodigo == Equipo(p, local) && x.VisitanteCodigo == Equipo(p, visitante));

    private static async Task Exito(Task<HttpResponseMessage> respuesta) =>
        Assert.Equal(HttpStatusCode.OK, (await respuesta).StatusCode);

    /// <summary>1–0 de EQ-03 (J-31) y 2–0 de EQ-01 (J-11 dos goles, J-12 una amarilla), cada uno guardado como versión 1.</summary>
    private static async Task GuardarVersionesUno(EntornoDePlanilla e, string p)
    {
        await Exito(e.Guardar(RutaDelPrimerPartido(p), new PlanillaJ(
            0, "Jugado", new(2, 0), TarjetasInformadas: true,
            Goles: [new(Jugador(p, 1, 1), 2)], Tarjetas: [new(Jugador(p, 1, 2), "Amarilla", 1)])));
        await Exito(e.Guardar(RutaDePlanilla("liga-a", p, 1, Equipo(p, 3), Equipo(p, 4)), new PlanillaJ(
            0, "Jugado", new(1, 0), TarjetasInformadas: true, Goles: [new(Jugador(p, 3, 1), 1)])));
    }

    /// <summary>La corrección de la versión 1 del primer partido a 1–1, con otras filas de goles y de tarjetas.</summary>
    private static PlanillaJ Correccion1a1(string p) => new(
        1, "Jugado", new(1, 1), TarjetasInformadas: true,
        Goles: [new(Jugador(p, 1, 1), 1), new(Jugador(p, 2, 1), 1)], Tarjetas: [new(Jugador(p, 2, 2), "Roja", 1)]);

    [Fact] // T-12 a (puerto): solo la versión vigente, con sus filas; una planilla retirada vuelve como Programado y sin filas
    public async Task T12a_el_puerto_devuelve_solo_la_version_vigente_con_sus_filas_y_una_retirada_como_Programado()
    {
        await using var e = new EntornoDePlanilla(db);
        var p = await e.Preparar();
        await GuardarVersionesUno(e, p);
        await Exito(e.Guardar(RutaDelPrimerPartido(p), Correccion1a1(p)));
        await Exito(e.Guardar(RutaDePlanilla("liga-a", p, 1, Equipo(p, 3), Equipo(p, 4)), Retirada(1)));

        var datos = await Leer(e.Services, p);

        Assert.NotNull(datos);
        // Versión 2 del primer partido: 1–1, con las filas de la versión 2 y ninguna de la versión 1.
        var corregido = Partido(datos, p, 1, 2);
        Assert.Equal(EstadoDePartidoParaEstadisticas.Jugado, corregido.Estado);
        var planilla = Assert.IsType<PlanillaVigenteParaEstadisticas>(corregido.Planilla);
        Assert.Equal(2, planilla.Version);
        Assert.Equal((1, 1), (planilla.MarcadorLocal, planilla.MarcadorVisitante));
        Assert.Equal(
            new (TipoDeGolParaEstadisticas, string, string?, int)[]
            {
                (TipoDeGolParaEstadisticas.Jugador, Equipo(p, 1), Jugador(p, 1, 1), 1),
                (TipoDeGolParaEstadisticas.Jugador, Equipo(p, 2), Jugador(p, 2, 1), 1),
            }.Order().ToArray(),
            planilla.Goles.Select(g => (g.Tipo, g.EquipoAcreditadoCodigo, g.JugadorCodigo, g.Cantidad)).Order().ToArray());
        var tarjeta = Assert.Single(planilla.Tarjetas);
        Assert.Equal((Jugador(p, 2, 2), ColorDeTarjetaParaEstadisticas.Roja, 1), (tarjeta.JugadorCodigo, tarjeta.Color, tarjeta.Cantidad));
        Assert.True(planilla.TarjetasInformadas);

        // Planilla retirada: el partido es Programado y no trae filas de ninguna versión.
        var retirado = Partido(datos, p, 3, 4);
        Assert.Equal(EstadoDePartidoParaEstadisticas.Programado, retirado.Estado);
        var vacia = Assert.IsType<PlanillaVigenteParaEstadisticas>(retirado.Planilla);
        Assert.Equal(2, vacia.Version);
        Assert.Empty(vacia.Goles);
        Assert.Empty(vacia.Tarjetas);

        // Un partido sin planilla (versión 0) no trae planilla.
        var sinPlanilla = Partido(datos, p, 1, 3);
        Assert.Equal(EstadoDePartidoParaEstadisticas.Programado, sinPlanilla.Estado);
        Assert.Null(sinPlanilla.Planilla);

        Assert.Equal(p, datos.Codigo);
        Assert.Equal(4, datos.Equipos.Count);
        Assert.Equal(12, datos.Jugadores.Count);
        Assert.Null(datos.Reglamento);
    }

    [Fact] // el torneo que no existe en el tenant devuelve nulo
    public async Task El_puerto_devuelve_nulo_si_el_torneo_no_existe_en_la_liga()
    {
        await using var e = new EntornoDePlanilla(db);
        await e.Preparar();

        Assert.Null(await Leer(e.Services, $"NO-EXISTE-{Prefijo()}"));
    }

    [Fact] // T-16 a (puerto): la lectura retenida ve la foto de antes de la corrección; una lectura nueva ve la de después
    public async Task T16a_la_lectura_retenida_devuelve_los_datos_de_antes_de_la_correccion_y_una_nueva_los_de_despues()
    {
        var compuerta = new CompuertaDeLecturaConsistente();
        await using var e = new EntornoDePlanilla(db, ajustarAnfitrion: b => b.ConfigureTestServices(
            s => s.ConfigureDbContext<GoalFlowDbContext>(o => o.AddInterceptors(compuerta))));
        var p = await e.Preparar();
        await GuardarVersionesUno(e, p);
        var antes = JsonSerializer.Serialize(await Leer(e.Services, p), Json);

        // La lectura marcada se detiene después de su primer comando del puerto.
        var retenida = Task.Run(async () =>
        {
            compuerta.Activar();
            var datos = await Leer(e.Services, p);
            compuerta.Anotar("puerto-devuelto");
            return JsonSerializer.Serialize(datos, Json);
        });
        await compuerta.Retenida.WaitAsync(TimeSpan.FromSeconds(60));

        // Mientras está retenida se confirma una corrección, por la API real.
        await Exito(e.Guardar(RutaDelPrimerPartido(p), Correccion1a1(p)));
        compuerta.Anotar("correccion-confirmada");
        compuerta.Liberar();
        var durante = await retenida.WaitAsync(TimeSpan.FromSeconds(60));

        var despues = JsonSerializer.Serialize(await Leer(e.Services, p), Json);

        // El orden de los eventos: retenida, corrección confirmada, liberada, devuelta.
        var orden = compuerta.Eventos.ToList();
        Assert.Equal(["puerto-retenido", "correccion-confirmada", "puerto-liberado", "puerto-devuelto"], orden);
        Assert.NotEqual(antes, despues);
        Assert.Equal(antes, durante);
    }
}
