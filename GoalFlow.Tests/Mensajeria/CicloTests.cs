using System.Collections.Concurrent;
using GoalFlow.Application.Mensajeria;
using GoalFlow.Application.Persistencia;
using GoalFlow.Domain.Tenancy;
using GoalFlow.Infrastructure.Ligas;
using GoalFlow.Infrastructure.Mensajeria;
using GoalFlow.Infrastructure.Persistencia;
using GoalFlow.Tests.Isolation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using static GoalFlow.Tests.Isolation.BaseDeAislamiento;
using static GoalFlow.Tests.Mensajeria.AyudantesDeMensajeria;

namespace GoalFlow.Tests.Mensajeria;

/// <summary>Spec 004: el ciclo en segundo plano de punta a punta, contra PostgreSQL 17 como goalflow_app.</summary>
[Collection(ColeccionDeAislamiento.Nombre)]
public class CicloTests(BaseDeAislamiento db)
{
    private const string Marcador = "MARCADOR-SECRETO-7f3a";

    private Task EsperarFila(string consumidor, Func<FilaDeEntrega, bool> condicion, string descripcion) =>
        Esperar(async () => (await db.LeerEntregas(consumidor)).Any(condicion), descripcion);

    private Task EsperarEstado(string consumidor, string estado, string descripcion) =>
        EsperarFila(consumidor, f => f.Estado == estado, descripcion);

    private static Action<Dictionary<string, string?>> Con(params (string Clave, string? Valor)[] valores) =>
        c =>
        {
            foreach (var (clave, valor) in valores) c[$"Mensajeria:{clave}"] = valor;
        };

    [Fact]
    public async Task T03_tras_un_reinicio_el_segundo_host_completa_lo_que_dejo_el_primero()
    {
        var nombre = NombreUnico("t03");

        await using (var primero = await AnfitrionDeMensajeria.IniciarAsync(
            db, s => s.AddConsumidorDePrueba<RanuraA>(nombre), Con(("CicloHabilitado", "false"))))
        {
            // R-21: con el ciclo deshabilitado no corre ninguna pasada.
            await Esperar(() => primero.Ciclo.ExecuteTask!.IsCompleted, "el ciclo deshabilitado termina enseguida");
            Assert.Equal(0, primero.Ciclo.PasadasCompletadas);

            for (var i = 0; i < 3; i++)
                await ProducirAsync(primero.Services, LigaA, Guid.NewGuid(), 1, null, confirmar: true);
        } // Se libera: lo que había en memoria se pierde.

        Assert.All(await db.LeerEntregas(nombre), f => Assert.Equal("Pendiente", f.Estado));
        Assert.Equal(3, (await db.LeerEntregas(nombre)).Count);

        ConsumidorRegistrado<RanuraA> segundo = null!;
        await using var host2 = await AnfitrionDeMensajeria.IniciarAsync(db, s => segundo = s.AddConsumidorDePrueba<RanuraA>(nombre));

        await Esperar(async () => (await db.LeerEntregas(nombre)).All(f => f.Estado == "Completada"), "las tres entregas se completan");
        Assert.Equal(3, segundo.Comportamiento.Corridas);
    }

    [Fact]
    public async Task T05_una_entrega_sin_pista_la_completa_el_temporizador()
    {
        var nombre = NombreUnico("t05");
        ConsumidorRegistrado<RanuraA> a = null!;
        await using var host = await AnfitrionDeMensajeria.IniciarAsync(db, s => a = s.AddConsumidorDePrueba<RanuraA>(nombre));
        await Esperar(() => host.Ciclo.PasadasCompletadas >= 1, "termina la primera pasada y el ciclo queda en reposo");

        // Insertada sin pasar por el publicador: ninguna pista pudo despertar al ciclo (como si el canal lleno la hubiera perdido).
        await db.InsertarEntrega(LigaA, nombre);

        await EsperarEstado(nombre, "Completada", "el temporizador encuentra la entrega");
        Assert.Equal(1, a.Comportamiento.Corridas);
    }

    [Fact]
    public async Task T06_el_ciclo_completa_cada_liga_en_su_scope_con_su_tenant()
    {
        var nombre = NombreUnico("t06");
        ConsumidorRegistrado<RanuraA> a = null!;
        await using var host = await AnfitrionDeMensajeria.IniciarAsync(db, s => a = s.AddConsumidorDePrueba<RanuraA>(nombre));
        var deA = Guid.NewGuid();
        var deB = Guid.NewGuid();

        await ProducirAsync(host.Services, LigaA, deA, 1, null, confirmar: true);
        await ProducirAsync(host.Services, LigaB, deB, 1, null, confirmar: true);

        await Esperar(async () => (await db.LeerEntregas(nombre)) is { Count: 2 } filas && filas.All(f => f.Estado == "Completada"),
            "se completan las entregas de las dos ligas");
        var llamadas = a.Comportamiento.Llamadas;
        Assert.Equal(2, llamadas.Count);
        Assert.All(llamadas, l => Assert.Equal(l.Tenant, l.TenantDelScope)); // El scope lleva el tenant de su propia entrega.
        Assert.Equal(LigaA, Assert.Single(llamadas, l => l.AgregadoId == deA).TenantDelScope);
        Assert.Equal(LigaB, Assert.Single(llamadas, l => l.AgregadoId == deB).TenantDelScope);
    }

    [Fact]
    public async Task T06_un_scope_de_A_no_ve_ni_reclama_la_entrega_de_B()
    {
        ConsumidorRegistrado<RanuraA> a = null!;
        await using var servicios = db.CrearServicios(s => a = s.AddConsumidorDePrueba<RanuraA>());
        await ProducirAsync(servicios, LigaA, Guid.NewGuid(), 1, null, confirmar: true);
        await ProducirAsync(servicios, LigaB, Guid.NewGuid(), 1, null, confirmar: true);
        var filaDeB = Assert.Single(await db.LeerEntregas(a.Nombre), f => f.TenantId == LigaB.Value);

        await using var scope = servicios.ScopeDe(LigaA);
        await using var transaccion = await scope.Iniciar();
        var visibles = await scope.Contexto().EntregasEventos.AsNoTracking().Where(e => e.Consumidor == a.Nombre).ToListAsync();
        var operaciones = scope.ServiceProvider.GetRequiredService<OperacionesDeEntrega>();
        var candidataDeB = new EntregaCandidata(
            filaDeB.Id, LigaB, EventoDePrueba.NombreDelEvento, filaDeB.AgregadoId, filaDeB.Version, filaDeB.Consumidor, 0);

        Assert.Equal(LigaA, Assert.Single(visibles).TenantId);
        Assert.DoesNotContain(await operaciones.ListarCandidatasAsync(CancellationToken.None), c => c.Id == filaDeB.Id);
        Assert.Null(await operaciones.ReclamarAsync(candidataDeB, CancellationToken.None)); // Cero filas afectadas.
        await transaccion.ConfirmarAsync(CancellationToken.None);
        Assert.Equal("Pendiente", Assert.Single(await db.LeerEntregas(a.Nombre), f => f.Id == filaDeB.Id).Estado);
    }

    [Fact]
    public async Task T14_un_consumidor_que_siempre_falla_corre_tres_veces_con_backoff_creciente_y_luego_queda_Fallida()
    {
        var nombre = NombreUnico("t14");
        var registros = new CapturaDeRegistros();
        ConsumidorRegistrado<RanuraA> a = null!;
        await using var host = await AnfitrionDeMensajeria.IniciarAsync(
            db,
            s =>
            {
                a = s.AddConsumidorDePrueba<RanuraA>(nombre);
                a.Comportamiento.Accion = (_, _, _) => throw new InvalidOperationException("siempre falla");
            },
            Con(("MaximoDeIntentos", "3"), ("BackoffInicial", "00:00:00.5"), ("PeriodoDelTemporizador", "00:00:00.25")),
            registros);

        await ProducirAsync(host.Services, LigaA, Guid.NewGuid(), 1, null, confirmar: true);

        await EsperarEstado(nombre, "Fallida", "la entrega queda Fallida");
        var llamadas = a.Comportamiento.Llamadas;
        Assert.Equal(3, llamadas.Count);
        Assert.Equal([1, 2, 3], llamadas.Select(l => l.Intento));
        // Backoff 0,5 s y 1 s: cada espera es al menos la duración calculada (con una pequeña tolerancia de medición).
        Assert.True(llamadas[1].Cuando - llamadas[0].Cuando >= TimeSpan.FromMilliseconds(450));
        Assert.True(llamadas[2].Cuando - llamadas[1].Cuando >= TimeSpan.FromMilliseconds(950));
        Assert.Contains(registros.Entradas, e => e.Nivel == LogLevel.Warning && e.Texto.Contains("intentos agotados"));
        Assert.Equal("InvalidOperationException", Assert.Single(await db.LeerEntregas(nombre)).UltimoError);

        await Esperar(() => host.Ciclo.PasadasCompletadas >= 4, "pasan varias pasadas más");
        Assert.Equal(3, a.Comportamiento.Corridas); // Nunca una cuarta.
    }

    [Fact]
    public void T14_el_backoff_es_30_s_por_2_a_la_n_menos_1_con_tope_de_30_min()
    {
        var operaciones = new OperacionesDeEntrega(
            null!, Options.Create(new OpcionesDeMensajeria()), [], NullLogger<OperacionesDeEntrega>.Instance);

        Assert.Equal(
            [30, 60, 120, 240, 480, 960, 1800, 1800],
            Enumerable.Range(1, 8).Select(i => (int)operaciones.CalcularBackoff(i).TotalSeconds));
    }

    [Fact]
    public async Task T16_un_fallo_permanente_corre_una_vez_y_queda_Fallida()
    {
        var nombre = NombreUnico("t16");
        ConsumidorRegistrado<RanuraA> a = null!;
        await using var host = await AnfitrionDeMensajeria.IniciarAsync(db, s =>
        {
            a = s.AddConsumidorDePrueba<RanuraA>(nombre);
            a.Comportamiento.Accion = (_, _, _) => throw new FalloPermanenteException("razon-x");
        });

        await ProducirAsync(host.Services, LigaA, Guid.NewGuid(), 1, null, confirmar: true);

        await EsperarEstado(nombre, "Fallida", "el fallo permanente termina la entrega");
        Assert.Equal(1, a.Comportamiento.Corridas);
        var fila = Assert.Single(await db.LeerEntregas(nombre));
        Assert.Equal(1, fila.Intentos);
        Assert.Equal("FalloPermanenteException:razon-x", fila.UltimoError);
    }

    [Fact]
    public async Task T17_el_timeout_cancela_al_consumidor_y_la_entrega_vuelve_a_Pendiente()
    {
        var nombre = NombreUnico("t17");
        ConsumidorRegistrado<RanuraA> a = null!;
        await using var host = await AnfitrionDeMensajeria.IniciarAsync(
            db,
            s =>
            {
                a = s.AddConsumidorDePrueba<RanuraA>(nombre);
                a.Comportamiento.Accion = (_, _, ct) => Task.Delay(TimeSpan.FromSeconds(10), ct);
            },
            Con(("DuracionDelLease", "00:00:05"), ("TimeoutDelConsumidor", "00:00:01"), ("BackoffInicial", "00:01:00")));

        await ProducirAsync(host.Services, LigaA, Guid.NewGuid(), 1, null, confirmar: true);

        await EsperarFila(nombre, f => f.Estado == "Pendiente" && f.Intentos == 1, "el timeout devuelve la entrega a Pendiente");
        Assert.Equal("TimeoutException", Assert.Single(await db.LeerEntregas(nombre)).UltimoError);
        Assert.Equal(1, a.Comportamiento.Corridas);
    }

    [Fact]
    public async Task T18_al_apagar_el_host_el_consumidor_se_cancela_y_no_se_escribe_nada()
    {
        var nombre = NombreUnico("t18");
        var iniciado = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tokenCancelado = false;
        ConsumidorRegistrado<RanuraA> a = null!;
        var host = await AnfitrionDeMensajeria.IniciarAsync(
            db,
            s =>
            {
                a = s.AddConsumidorDePrueba<RanuraA>(nombre);
                a.Comportamiento.Accion = async (_, _, ct) =>
                {
                    iniciado.SetResult();
                    try
                    {
                        await Task.Delay(Timeout.Infinite, ct);
                    }
                    finally
                    {
                        tokenCancelado = ct.IsCancellationRequested;
                    }
                };
            },
            Con(("TimeoutDelConsumidor", "00:00:08"), ("DuracionDelLease", "00:00:10")));
        await using var _ = host;

        await ProducirAsync(host.Services, LigaA, Guid.NewGuid(), 1, null, confirmar: true);
        await iniciado.Task.WaitAsync(TopeDeEspera);
        var antes = Assert.Single(await db.LeerEntregas(nombre));
        Assert.Equal("EnProceso", antes.Estado);

        await host.Host.StopAsync();

        Assert.True(tokenCancelado);
        var despues = Assert.Single(await db.LeerEntregas(nombre));
        Assert.Equal("EnProceso", despues.Estado); // No volvió a Pendiente: el apagado no es un fallo del consumidor.
        Assert.Equal(antes.Intentos, despues.Intentos);
        Assert.Equal(antes.Token, despues.Token);
        Assert.Null(despues.UltimoError);
    }

    [Fact]
    public async Task T19_un_consumidor_que_falla_no_afecta_a_otro_del_mismo_evento()
    {
        var sano = NombreUnico("t19-sano");
        var roto = NombreUnico("t19-roto");
        ConsumidorRegistrado<RanuraA> a = null!;
        ConsumidorRegistrado<RanuraB> b = null!;
        await using var host = await AnfitrionDeMensajeria.IniciarAsync(
            db,
            s =>
            {
                a = s.AddConsumidorDePrueba<RanuraA>(sano);
                b = s.AddConsumidorDePrueba<RanuraB>(roto);
                b.Comportamiento.Accion = (_, _, _) => throw new InvalidOperationException("roto");
            },
            Con(("MaximoDeIntentos", "3"), ("BackoffInicial", "00:00:00.2"), ("PeriodoDelTemporizador", "00:00:00.25")));

        await ProducirAsync(host.Services, LigaA, Guid.NewGuid(), 1, null, confirmar: true);

        await EsperarEstado(roto, "Fallida", "el consumidor roto agota sus intentos");
        var filaSana = Assert.Single(await db.LeerEntregas(sano));
        var filaRota = Assert.Single(await db.LeerEntregas(roto));
        Assert.Equal("Completada", filaSana.Estado);
        Assert.Equal(1, filaSana.Intentos);
        Assert.Equal(3, filaRota.Intentos);
        Assert.Equal(1, a.Comportamiento.Corridas);
        Assert.Equal(3, b.Comportamiento.Corridas);
    }

    [Fact]
    public async Task T21_el_arranque_falla_con_nombres_repetidos_timeout_igual_al_lease_o_capacidad_cero()
    {
        await using (var repetidos = AnfitrionDeMensajeria.Construir(db, s =>
        {
            s.AddConsumidorDePrueba<RanuraA>("mismo-nombre");
            s.AddConsumidorDePrueba<RanuraB>("mismo-nombre");
        }))
        {
            var error = await Assert.ThrowsAsync<OptionsValidationException>(() => repetidos.Host.StartAsync());
            Assert.Contains("mismo-nombre", error.Message);
        }

        await using (var igual = AnfitrionDeMensajeria.Construir(
            db, null, Con(("DuracionDelLease", "00:00:05"), ("TimeoutDelConsumidor", "00:00:05"))))
            await Assert.ThrowsAsync<OptionsValidationException>(() => igual.Host.StartAsync());

        await using (var cero = AnfitrionDeMensajeria.Construir(db, null, Con(("CapacidadDelCanal", "0"))))
            await Assert.ThrowsAsync<OptionsValidationException>(() => cero.Host.StartAsync());

        // Con los valores por defecto del código (sin ninguna clave de Mensajeria) el host arranca.
        await using var porDefecto = await AnfitrionDeMensajeria.IniciarAsync(
            db, s => s.AddConsumidorDePrueba<RanuraA>(), c =>
            {
                foreach (var clave in c.Keys.Where(k => k.StartsWith("Mensajeria:", StringComparison.Ordinal)).ToList())
                    c.Remove(clave);
            });
        Assert.Equal(64, porDefecto.Services.GetRequiredService<CanalDeAvisos>().Configuracion.Capacity);
    }

    [Fact]
    public async Task T23_el_mensaje_de_una_excepcion_no_se_guarda_ni_se_registra_en_ningun_nivel()
    {
        var registros = new CapturaDeRegistros();
        var reintentable = NombreUnico("t23-reintentable");
        var permanente = NombreUnico("t23-permanente");
        await using var host = await AnfitrionDeMensajeria.IniciarAsync(
            db,
            s =>
            {
                var a = s.AddConsumidorDePrueba<RanuraA>(reintentable);
                a.Comportamiento.Accion = (_, _, _) =>
                    throw new InvalidOperationException(Marcador, new ArgumentException(Marcador));
                var b = s.AddConsumidorDePrueba<RanuraB>(permanente);
                b.Comportamiento.Accion = (_, _, _) =>
                    throw new FalloPermanenteException("prueba", new InvalidOperationException(Marcador));
            },
            Con(("BackoffInicial", "00:01:00")),
            registros);

        await ProducirAsync(host.Services, LigaA, Guid.NewGuid(), 1, null, confirmar: true);

        await EsperarEstado(permanente, "Fallida", "el fallo permanente termina");
        await EsperarFila(reintentable, f => f.Estado == "Pendiente" && f.Intentos == 1, "el fallo reintentable vuelve a Pendiente");

        var filaReintentable = Assert.Single(await db.LeerEntregas(reintentable));
        var filaPermanente = Assert.Single(await db.LeerEntregas(permanente));
        Assert.Equal("InvalidOperationException", filaReintentable.UltimoError);
        Assert.Contains("FalloPermanenteException", filaPermanente.UltimoError);
        Assert.Contains("prueba", filaPermanente.UltimoError);
        Assert.DoesNotContain(Marcador, filaReintentable.UltimoError);
        Assert.DoesNotContain(Marcador, filaPermanente.UltimoError);

        await host.Host.StopAsync();
        var entradas = registros.Entradas;
        Assert.DoesNotContain(entradas, e => e.Texto.Contains(Marcador));
        // La captura es real: en esas mismas entradas sí aparecen los tipos de las excepciones.
        Assert.Contains(entradas, e => e.Texto.Contains("System.InvalidOperationException"));
        Assert.Contains(entradas, e => e.Texto.Contains("System.ArgumentException"));
        Assert.Contains(entradas, e => e.Texto.Contains("FalloPermanenteException"));
    }

    private sealed class ServicioQueCapturaScoped(GoalFlowDbContext contexto) : BackgroundService
    {
        public GoalFlowDbContext Contexto { get; } = contexto;

        protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
    }

    [Fact]
    public async Task T24_en_Production_la_validacion_de_DI_acepta_el_ciclo_y_rechaza_un_servicio_que_captura_lo_scoped()
    {
        await using var correcto = await AnfitrionDeMensajeria.IniciarAsync(db);
        Assert.NotNull(correcto.Ciclo);

        var error = Assert.Throws<AggregateException>(() => AnfitrionDeMensajeria.Construir(
            db, s => s.AddHostedService<ServicioQueCapturaScoped>()));
        Assert.Contains(error.InnerExceptions, e => e.Message.Contains(nameof(GoalFlowDbContext)));
    }

    /// <summary>Delega en la fuente real, salvo que falla en la primera llamada.</summary>
    private sealed class FuenteQueFallaUnaVez(GoalFlowDbContext contexto, ContadorDeLlamadas contador) : IFuenteDeTenants
    {
        public Task<IReadOnlyList<TenantId>> ListarAsync(CancellationToken cancellationToken) =>
            contador.Siguiente() == 1
                ? throw new InvalidOperationException(Marcador)
                : new FuenteDeTenants(contexto).ListarAsync(cancellationToken);
    }

    private sealed class ContadorDeLlamadas
    {
        private int _llamadas;

        public int Siguiente() => Interlocked.Increment(ref _llamadas);

        public int Llamadas => Volatile.Read(ref _llamadas);
    }

    [Fact]
    public async Task T33_una_fuente_de_tenants_que_falla_no_tumba_el_host_y_la_siguiente_pasada_trabaja()
    {
        var nombre = NombreUnico("t33");
        var registros = new CapturaDeRegistros();
        ConsumidorRegistrado<RanuraA> a = null!;
        await using var host = await AnfitrionDeMensajeria.IniciarAsync(
            db,
            s =>
            {
                a = s.AddConsumidorDePrueba<RanuraA>(nombre);
                s.AddSingleton<ContadorDeLlamadas>();
                s.AddScoped<IFuenteDeTenants, FuenteQueFallaUnaVez>();
            },
            registros: registros);
        await db.InsertarEntrega(LigaA, nombre);

        await EsperarEstado(nombre, "Completada", "una pasada posterior completa la entrega");

        Assert.False(host.Ciclo.ExecuteTask!.IsCompleted); // El host sigue vivo.
        Assert.Contains(registros.Entradas, e =>
            e.Nivel == LogLevel.Warning && e.Categoria.EndsWith(nameof(CicloDePostGuardado)) && e.Texto.Contains("System.InvalidOperationException"));
        Assert.DoesNotContain(registros.Entradas, e => e.Texto.Contains(Marcador));
        Assert.True(host.Services.GetRequiredService<ContadorDeLlamadas>().Llamadas >= 2);
        Assert.Equal(1, a.Comportamiento.Corridas);
    }

    [Fact]
    public async Task T34_el_ciclo_nunca_tiene_mas_de_una_conexion_abierta_aunque_el_consumidor_lea_la_base()
    {
        var nombre = NombreUnico("t34");
        var conexiones = new ContadorDeConexiones();
        var lecturas = new ConcurrentBag<(TenantId Tenant, string Nombre)>();
        var cadenaConUnaConexion = new NpgsqlConnectionStringBuilder(db.CadenaApp) { MaxPoolSize = 1, Timeout = 5 }.ConnectionString;

        // Dos entregas por liga, ya en la base cuando arranca el host.
        for (var i = 1; i <= 2; i++)
        {
            await db.InsertarEntrega(LigaA, nombre, version: i);
            await db.InsertarEntrega(LigaB, nombre, version: i);
        }

        // (2) La misma corrida con Maximum Pool Size = 1: cualquier solapamiento agotaría el pool y vencería el timeout.
        await using var host = await AnfitrionDeMensajeria.IniciarAsync(
            db,
            s =>
            {
                var a = s.AddConsumidorDePrueba<RanuraA>(nombre);
                a.Comportamiento.Accion = async (entrega, proveedor, ct) =>
                {
                    await using var transaccion = await proveedor.GetRequiredService<IUnidadDeTrabajo>().IniciarAsync(ct);
                    var equipos = await proveedor.GetRequiredService<GoalFlowDbContext>().Equipos.AsNoTracking().ToListAsync(ct);
                    foreach (var equipo in equipos) lecturas.Add((equipo.TenantId, equipo.Nombre));
                    await transaccion.ConfirmarAsync(ct);
                };
                s.ConfigureDbContext<GoalFlowDbContext>(o => o.AddInterceptors(conexiones));
            },
            c => c["ConnectionStrings:GoalFlow"] = cadenaConUnaConexion);

        await Esperar(async () => (await db.LeerEntregas(nombre)) is { Count: 4 } filas && filas.All(f => f.Estado == "Completada"),
            "las cuatro entregas se completan con un pool de una sola conexión");
        Assert.Equal(1, conexiones.Maximo);
        Assert.True(conexiones.Aperturas > 4);
        Assert.Equal(LigaA, lecturas.Where(l => l.Nombre == EquipoDeA).Select(l => l.Tenant).Distinct().Single());
        Assert.DoesNotContain(lecturas, l => l.Tenant == LigaA && l.Nombre == EquipoDeB);
    }

    [Fact]
    public async Task T35_el_ciclo_ignora_las_entregas_de_consumidores_que_no_conoce()
    {
        var registrado = NombreUnico("t35-registrado");
        var desconocido = NombreUnico("t35-no-registrado");
        ConsumidorRegistrado<RanuraA> a = null!;
        await using var host = await AnfitrionDeMensajeria.IniciarAsync(db, s => a = s.AddConsumidorDePrueba<RanuraA>(registrado));
        await db.InsertarEntrega(LigaA, desconocido);
        await db.InsertarEntrega(LigaA, registrado);

        await EsperarEstado(registrado, "Completada", "la entrega del consumidor registrado se completa");
        var pasadas = host.Ciclo.PasadasCompletadas;
        await Esperar(() => host.Ciclo.PasadasCompletadas >= pasadas + 3, "pasan varias pasadas más");

        var fila = Assert.Single(await db.LeerEntregas(desconocido));
        Assert.Equal("Pendiente", fila.Estado);
        Assert.Equal(0, fila.Intentos);
        Assert.Null(fila.Token);
        Assert.Equal(1, a.Comportamiento.Corridas);
    }
}
