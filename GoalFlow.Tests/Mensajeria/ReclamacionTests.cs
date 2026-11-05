using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using GoalFlow.Application.Persistencia;
using GoalFlow.Domain.Tenancy;
using GoalFlow.Infrastructure.Mensajeria;
using GoalFlow.Infrastructure.Persistencia;
using GoalFlow.Tests.Isolation;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit.Abstractions;
using static GoalFlow.Tests.Isolation.BaseDeAislamiento;
using static GoalFlow.Tests.Mensajeria.AyudantesDeMensajeria;

namespace GoalFlow.Tests.Mensajeria;

/// <summary>Registra el SQL de cada comando: texto y tipo de cada parámetro.</summary>
public sealed class RegistroDeComandos : DbCommandInterceptor
{
    public sealed record Comando(string Texto, IReadOnlyList<(string Nombre, DbType Tipo, Type? TipoClr)> Parametros);

    private readonly ConcurrentQueue<Comando> _comandos = new();

    public IReadOnlyList<Comando> Comandos => [.. _comandos];

    private void Anotar(DbCommand comando) =>
        _comandos.Enqueue(new Comando(
            comando.CommandText,
            [.. comando.Parameters.Cast<DbParameter>().Select(p => (p.ParameterName, p.DbType, p.Value?.GetType()))]));

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Anotar(command);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Anotar(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Anotar(command);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Anotar(command);
        return ValueTask.FromResult(result);
    }
}

/// <summary>Spec 004 T-07 a T-11 y T-15: reclamación con lease, fencing y transacción obligatoria.</summary>
[Collection(ColeccionDeAislamiento.Nombre)]
public class ReclamacionTests(BaseDeAislamiento db, ITestOutputHelper salida)
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    /// <summary>Un trabajador de prueba: su propio scope, DbContext, conexión y transacción.</summary>
    private sealed class Trabajador : IAsyncDisposable
    {
        private readonly AsyncServiceScope _scope;
        private readonly ITransaccion _transaccion;

        private Trabajador(AsyncServiceScope scope, ITransaccion transaccion)
        {
            _scope = scope;
            _transaccion = transaccion;
            Operaciones = scope.ServiceProvider.GetRequiredService<OperacionesDeEntrega>();
        }

        public OperacionesDeEntrega Operaciones { get; }

        public static async Task<Trabajador> IniciarAsync(IServiceProvider servicios, TenantId tenant)
        {
            var scope = servicios.ScopeDe(tenant);
            return new Trabajador(scope, await scope.Iniciar());
        }

        public Task ConfirmarAsync() => _transaccion.ConfirmarAsync(Ct);

        public async ValueTask DisposeAsync()
        {
            await _transaccion.DisposeAsync();
            await _scope.DisposeAsync();
        }
    }

    private static EntregaCandidata CandidataDe(FilaDeEntrega f) =>
        new(f.Id, new TenantId(f.TenantId), EventoDePrueba.NombreDelEvento, f.AgregadoId, f.Version, f.Consumidor, f.Intentos);

    private sealed record Escenario(ServiceProvider Servicios, ConsumidorRegistrado<RanuraA> A, FilaDeEntrega Fila) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Servicios.DisposeAsync();
    }

    private async Task<Escenario> Preparar(Action<OpcionesDeMensajeria>? ajustar = null, Action<IServiceCollection>? mas = null)
    {
        ConsumidorRegistrado<RanuraA> a = null!;
        var servicios = db.CrearServicios(s =>
        {
            a = s.AddConsumidorDePrueba<RanuraA>();
            if (ajustar is not null) s.Configure(ajustar);
            mas?.Invoke(s);
        });
        await ProducirAsync(servicios, LigaA, Guid.NewGuid(), 1, null, confirmar: true);
        return new Escenario(servicios, a, Assert.Single(await db.LeerEntregas(a.Nombre)));
    }

    private static void LeaseCorto(OpcionesDeMensajeria o)
    {
        o.DuracionDelLease = TimeSpan.FromSeconds(2);
        o.TimeoutDelConsumidor = TimeSpan.FromSeconds(1);
    }

    /// <summary>Reclama en una transacción propia que confirma. Null si no ganó.</summary>
    private async Task<EntregaReclamada?> ReclamarYConfirmar(Escenario e)
    {
        var fila = Assert.Single(await db.LeerEntregas(e.A.Nombre));
        await using var trabajador = await Trabajador.IniciarAsync(e.Servicios, LigaA);
        var reclamada = await trabajador.Operaciones.ReclamarAsync(CandidataDe(fila), Ct);
        if (reclamada is not null) await trabajador.ConfirmarAsync();
        return reclamada;
    }

    /// <summary>Sondea hasta que el lease vence por el reloj de la base y el reclamo prospera.</summary>
    private async Task<EntregaReclamada> ReclamarCuandoVenza(Escenario e)
    {
        EntregaReclamada? reclamada = null;
        await Esperar(async () => (reclamada = await ReclamarYConfirmar(e)) is not null, "el lease vence y el reclamo prospera");
        return reclamada!;
    }

    [Fact]
    public async Task T07_dos_trabajadores_reclaman_la_misma_fila_y_solo_gana_el_que_confirma_primero()
    {
        await using var e = await Preparar();
        await using var a = await Trabajador.IniciarAsync(e.Servicios, LigaA);
        await using var b = await Trabajador.IniciarAsync(e.Servicios, LigaA);

        var deA = await a.Operaciones.ReclamarAsync(CandidataDe(e.Fila), Ct);
        Assert.NotNull(deA);

        // B queda bloqueado en la fila que A modificó sin confirmar (Spec 004 C-04): el orden lo controla la prueba.
        var deB = Task.Run(() => b.Operaciones.ReclamarAsync(CandidataDe(e.Fila), Ct));
        await Esperar(async () => await db.ContarBloqueados() > 0, "B espera el bloqueo de la fila");
        Assert.False(deB.IsCompleted);

        await a.ConfirmarAsync();

        Assert.Null(await deB); // PostgreSQL reevalúa el WHERE de B tras el commit de A: afecta cero filas.
        var fila = Assert.Single(await db.LeerEntregas(e.A.Nombre));
        Assert.Equal(deA.Token, fila.Token);
        Assert.Equal("EnProceso", fila.Estado);
        Assert.Equal(1, fila.Intentos);
    }

    [Fact]
    public async Task T08_si_el_ganador_revierte_el_otro_trabajador_puede_reclamar()
    {
        await using var e = await Preparar();
        await using var b = await Trabajador.IniciarAsync(e.Servicios, LigaA);
        EntregaReclamada? deB;
        Task<EntregaReclamada?> espera;

        var a = await Trabajador.IniciarAsync(e.Servicios, LigaA);
        Assert.NotNull(await a.Operaciones.ReclamarAsync(CandidataDe(e.Fila), Ct));

        espera = Task.Run(() => b.Operaciones.ReclamarAsync(CandidataDe(e.Fila), Ct));
        await Esperar(async () => await db.ContarBloqueados() > 0, "B espera el bloqueo de la fila");
        Assert.False(espera.IsCompleted);

        await a.DisposeAsync(); // Sin confirmar: se revierte.
        deB = await espera;

        Assert.NotNull(deB);
        await b.ConfirmarAsync();
        var fila = Assert.Single(await db.LeerEntregas(e.A.Nombre));
        Assert.Equal(deB.Token, fila.Token);
        Assert.Equal(1, fila.Intentos);
    }

    [Fact]
    public async Task T09_el_lease_vence_por_el_reloj_de_la_base_y_el_SQL_no_envia_timestamps()
    {
        var comandos = new RegistroDeComandos();
        await using var e = await Preparar(
            o =>
            {
                LeaseCorto(o);
                o.MaximoDeIntentos = 10;
                o.BackoffInicial = TimeSpan.FromSeconds(1);
            },
            s => s.ConfigureDbContext<GoalFlowDbContext>(o => o.AddInterceptors(comandos)));

        // La creación la puso la base (default now()): nunca la aplicación.
        await using (var conexion = new NpgsqlConnection(db.CadenaSuperusuario))
        {
            await conexion.OpenAsync();
            await using var reloj = new NpgsqlCommand("select now()", conexion);
            var ahoraDeLaBase = (DateTime)(await reloj.ExecuteScalarAsync())!;
            Assert.True(e.Fila.CreadoEn <= ahoraDeLaBase && ahoraDeLaBase - e.Fila.CreadoEn < TimeSpan.FromMinutes(1));
            Assert.Equal(e.Fila.CreadoEn, e.Fila.ProximoIntentoEn);
        }

        Assert.NotNull(await ReclamarYConfirmar(e));
        Assert.Null(await ReclamarYConfirmar(e)); // Lease vigente: cero filas.

        var segunda = await ReclamarCuandoVenza(e); // Vencido: el otro reclamo prospera.
        Assert.Equal(2, segunda.Intentos);
        Assert.Equal(2, Assert.Single(await db.LeerEntregas(e.A.Nombre)).Intentos);

        await using (var falla = await Trabajador.IniciarAsync(e.Servicios, LigaA))
        {
            Assert.True(await falla.Operaciones.ReintentarAsync(segunda, "InvalidOperationException", Ct));
            await falla.ConfirmarAsync();
        }

        var tras = Assert.Single(await db.LeerEntregas(e.A.Nombre));
        Assert.Equal("Pendiente", tras.Estado);
        Assert.Equal("InvalidOperationException", tras.UltimoError);
        Assert.True(tras.ProximoIntentoEn > tras.CreadoEn);

        var tercera = await ReclamarCuandoVenza(e); // También espera el backoff.
        Assert.Equal(3, tercera.Intentos);
        await using (var completa = await Trabajador.IniciarAsync(e.Servicios, LigaA))
        {
            Assert.True(await completa.Operaciones.CompletarAsync(tercera, Ct));
            await completa.ConfirmarAsync();
        }

        Assert.Equal("Completada", Assert.Single(await db.LeerEntregas(e.A.Nombre)).Estado);

        var actualizaciones = comandos.Comandos
            .Where(c => c.Texto.Contains("UPDATE", StringComparison.OrdinalIgnoreCase) && c.Texto.Contains("entregas_eventos"))
            .ToList();
        foreach (var c in actualizaciones)
            salida.WriteLine(c.Texto + Environment.NewLine + string.Join(", ", c.Parametros.Select(p => $"{p.Nombre}:{p.Tipo}/{p.TipoClr?.Name}")) + Environment.NewLine);

        Assert.True(actualizaciones.Count >= 5, "reclamo x3, fallo y finalización deben haberse registrado");
        Assert.All(actualizaciones, c =>
        {
            Assert.Contains("now()", c.Texto);
            Assert.Contains("tenant_id", c.Texto); // El filtro Tenant también se aplica a ExecuteUpdateAsync.
            Assert.All(c.Parametros, p =>
            {
                Assert.NotEqual(typeof(DateTime), p.TipoClr);
                Assert.NotEqual(typeof(DateTimeOffset), p.TipoClr);
                Assert.DoesNotContain(p.Tipo, new[] { DbType.DateTime, DbType.DateTime2, DbType.DateTimeOffset, DbType.Time, DbType.Date });
            });
        });
    }

    [Fact]
    public async Task T10_un_trabajador_que_perdio_el_lease_no_puede_completar()
    {
        var captura = new CapturaDeRegistros();
        await using var e = await Preparar(LeaseCorto, s => s.AddLogging(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(captura)));

        var deA = await ReclamarYConfirmar(e);
        Assert.NotNull(deA);
        var deB = await ReclamarCuandoVenza(e);
        Assert.NotEqual(deA.Token, deB.Token);

        await using (var viejo = await Trabajador.IniciarAsync(e.Servicios, LigaA))
        {
            Assert.False(await viejo.Operaciones.CompletarAsync(deA, Ct));
            await viejo.ConfirmarAsync();
        }

        var fila = Assert.Single(await db.LeerEntregas(e.A.Nombre));
        Assert.Equal("EnProceso", fila.Estado);
        Assert.Equal(deB.Token, fila.Token);
        Assert.Contains(captura.Entradas, x => x.Nivel == LogLevel.Warning && x.Texto.Contains(deA.Id.ToString()));

        await using (var vigente = await Trabajador.IniciarAsync(e.Servicios, LigaA))
        {
            Assert.True(await vigente.Operaciones.CompletarAsync(deB, Ct));
            await vigente.ConfirmarAsync();
        }

        Assert.Equal("Completada", Assert.Single(await db.LeerEntregas(e.A.Nombre)).Estado);
    }

    [Fact]
    public async Task R22_la_lectura_de_candidatas_respeta_el_tope_de_lote()
    {
        ConsumidorRegistrado<RanuraA> a = null!;
        await using var servicios = db.CrearServicios(s =>
        {
            a = s.AddConsumidorDePrueba<RanuraA>();
            s.Configure<OpcionesDeMensajeria>(o => o.LotePorTenant = 10);
        });
        for (var i = 0; i < 12; i++)
            await ProducirAsync(servicios, LigaA, Guid.NewGuid(), 1, null, confirmar: true);
        Assert.Equal(12, (await db.LeerEntregas(a.Nombre)).Count);

        await using var trabajador = await Trabajador.IniciarAsync(servicios, LigaA);

        Assert.Equal(10, (await trabajador.Operaciones.ListarCandidatasAsync(Ct)).Count);
    }

    [Fact]
    public async Task T11_sin_transaccion_las_operaciones_lanzan_antes_de_enviar_ninguna_sentencia()
    {
        var comandos = new RegistroDeComandos();
        await using var e = await Preparar(null, s => s.ConfigureDbContext<GoalFlowDbContext>(o => o.AddInterceptors(comandos)));
        var antes = comandos.Comandos.Count;

        await using var scope = e.Servicios.ScopeDe(LigaA); // Sin transacción.
        var operaciones = scope.ServiceProvider.GetRequiredService<OperacionesDeEntrega>();
        var candidata = CandidataDe(e.Fila);
        var reclamada = new EntregaReclamada(
            candidata.Id, candidata.Tenant, candidata.TipoDeEvento, candidata.AgregadoId, candidata.Version, candidata.Consumidor, 1, Guid.NewGuid());

        await Assert.ThrowsAsync<TransaccionRequeridaException>(() => operaciones.ReclamarAsync(candidata, Ct));
        await Assert.ThrowsAsync<TransaccionRequeridaException>(() => operaciones.CompletarAsync(reclamada, Ct));
        await Assert.ThrowsAsync<TransaccionRequeridaException>(() => operaciones.ReintentarAsync(reclamada, "X", Ct));
        await Assert.ThrowsAsync<TransaccionRequeridaException>(() => operaciones.FallarAsync(reclamada, "X", Ct));
        await Assert.ThrowsAsync<TransaccionRequeridaException>(() => operaciones.AgotarAsync(candidata, Ct));
        await Assert.ThrowsAsync<TransaccionRequeridaException>(() => operaciones.ListarCandidatasAsync(Ct));

        Assert.Equal(antes, comandos.Comandos.Count);
        Assert.DoesNotContain(comandos.Comandos.Skip(antes), c => c.Texto.Contains("entregas_eventos"));
        // Y no fue una carrera perdida: la fila sigue intacta.
        Assert.Equal(0, Assert.Single(await db.LeerEntregas(e.A.Nombre)).Intentos);
    }

    [Fact]
    public async Task T15_los_reclamos_abandonados_cuentan_intentos_y_el_ultimo_se_marca_Fallida_sin_correr_el_consumidor()
    {
        await using var e = await Preparar(o =>
        {
            LeaseCorto(o);
            o.MaximoDeIntentos = 3;
        });

        for (var intento = 1; intento <= 3; intento++)
        {
            var reclamada = await ReclamarCuandoVenza(e); // "Se cae" tras reclamar: nunca completa.
            Assert.Equal(intento, reclamada.Intentos);
        }

        var fila = Assert.Single(await db.LeerEntregas(e.A.Nombre));
        Assert.Equal("EnProceso", fila.Estado);
        Assert.Equal(3, fila.Intentos);

        // Con el lease vigente, la fila todavía no está disponible.
        await using (var antes = await Trabajador.IniciarAsync(e.Servicios, LigaA))
            Assert.DoesNotContain(await antes.Operaciones.ListarCandidatasAsync(Ct), c => c.Id == fila.Id);

        EntregaCandidata? disponible = null;
        await Esperar(async () =>
        {
            await using var t = await Trabajador.IniciarAsync(e.Servicios, LigaA);
            disponible = (await t.Operaciones.ListarCandidatasAsync(Ct)).SingleOrDefault(c => c.Id == fila.Id);
            return disponible is not null;
        }, "el lease del último reclamo vence");

        await using (var pasada = await Trabajador.IniciarAsync(e.Servicios, LigaA))
        {
            Assert.Equal(3, disponible!.Intentos);
            Assert.Null(await pasada.Operaciones.ReclamarAsync(disponible, Ct)); // Nunca se reclama una cuarta vez.
            Assert.True(await pasada.Operaciones.AgotarAsync(disponible, Ct));
            await pasada.ConfirmarAsync();
        }

        var final = Assert.Single(await db.LeerEntregas(e.A.Nombre));
        Assert.Equal("Fallida", final.Estado);
        Assert.Equal(3, final.Intentos);
        Assert.Equal("IntentosAgotados", final.UltimoError);
        Assert.Equal(0, e.A.Comportamiento.Corridas);
    }
}
