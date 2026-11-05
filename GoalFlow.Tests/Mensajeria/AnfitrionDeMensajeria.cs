using System.Data.Common;
using GoalFlow.Infrastructure.Mensajeria;
using GoalFlow.Infrastructure.Persistencia;
using GoalFlow.Tests.Isolation;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GoalFlow.Tests.Mensajeria;

/// <summary>
/// Un host real (Production, con validación de DI al construir) con el registro de producción de persistencia y
/// mensajería, conectado como goalflow_app. Los valores de tiempo son cortos; cada prueba los ajusta.
/// </summary>
public sealed class AnfitrionDeMensajeria : IAsyncDisposable
{
    private bool _iniciado;

    private AnfitrionDeMensajeria(IHost host) => Host = host;

    public IHost Host { get; }

    public IServiceProvider Services => Host.Services;

    public CicloDePostGuardado Ciclo => Host.Services.GetServices<IHostedService>().OfType<CicloDePostGuardado>().Single();

    public static Dictionary<string, string?> ValoresDePrueba(BaseDeAislamiento db) => new()
    {
        ["ConnectionStrings:GoalFlow"] = db.CadenaApp,
        ["Mensajeria:CicloHabilitado"] = "true",
        ["Mensajeria:CapacidadDelCanal"] = "64",
        ["Mensajeria:PeriodoDelTemporizador"] = "00:00:01",
        ["Mensajeria:LotePorTenant"] = "10",
        ["Mensajeria:DuracionDelLease"] = "00:00:10",
        ["Mensajeria:TimeoutDelConsumidor"] = "00:00:05",
        ["Mensajeria:MaximoDeIntentos"] = "5",
        ["Mensajeria:BackoffInicial"] = "00:00:01",
        ["Mensajeria:BackoffMaximo"] = "00:00:30",
    };

    /// <summary>Construye el host sin arrancarlo: la validación de DI corre aquí (ValidateOnBuild).</summary>
    public static AnfitrionDeMensajeria Construir(
        BaseDeAislamiento db,
        Action<IServiceCollection>? servicios = null,
        Action<Dictionary<string, string?>>? ajustar = null,
        CapturaDeRegistros? registros = null)
    {
        var configuracion = ValoresDePrueba(db);
        ajustar?.Invoke(configuracion);

        var host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .UseEnvironment(Environments.Production)
            .UseDefaultServiceProvider(o =>
            {
                o.ValidateScopes = true;
                o.ValidateOnBuild = true;
            })
            .ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(configuracion))
            .ConfigureLogging(l =>
            {
                l.ClearProviders();
                if (registros is null) return;
                l.SetMinimumLevel(LogLevel.Trace);
                l.AddProvider(registros);
            })
            .ConfigureServices(s =>
            {
                s.AddGoalFlowPersistencia();
                s.AddGoalFlowMensajeria();
                servicios?.Invoke(s);
            })
            .Build();
        return new AnfitrionDeMensajeria(host);
    }

    public static async Task<AnfitrionDeMensajeria> IniciarAsync(
        BaseDeAislamiento db,
        Action<IServiceCollection>? servicios = null,
        Action<Dictionary<string, string?>>? ajustar = null,
        CapturaDeRegistros? registros = null)
    {
        var anfitrion = Construir(db, servicios, ajustar, registros);
        await anfitrion.Host.StartAsync();
        anfitrion._iniciado = true;
        return anfitrion;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_iniciado) await Host.StopAsync();
        }
        finally
        {
            Host.Dispose();
        }
    }
}

/// <summary>Cuenta las conexiones abiertas a la vez, entre todos los scopes (Spec 004 T-34).</summary>
public sealed class ContadorDeConexiones : DbConnectionInterceptor
{
    private int _abiertas;
    private int _maximo;

    public int Maximo => Volatile.Read(ref _maximo);

    public int Aperturas { get; private set; }

    private void Abrio()
    {
        var ahora = Interlocked.Increment(ref _abiertas);
        int visto;
        do
        {
            visto = Volatile.Read(ref _maximo);
        }
        while (ahora > visto && Interlocked.CompareExchange(ref _maximo, ahora, visto) != visto);
        Aperturas++;
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) => Abrio();

    public override Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Abrio();
        return Task.CompletedTask;
    }

    public override void ConnectionClosed(DbConnection connection, ConnectionEndEventData eventData) =>
        Interlocked.Decrement(ref _abiertas);

    public override Task ConnectionClosedAsync(DbConnection connection, ConnectionEndEventData eventData)
    {
        Interlocked.Decrement(ref _abiertas);
        return Task.CompletedTask;
    }
}
