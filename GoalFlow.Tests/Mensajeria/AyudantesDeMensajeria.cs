using System.Collections.Concurrent;
using GoalFlow.Application.Mensajeria;
using GoalFlow.Application.Persistencia;
using GoalFlow.Application.Tenancy;
using GoalFlow.Domain.Tenancy;
using GoalFlow.Domain.Torneos;
using GoalFlow.Infrastructure.Persistencia;
using GoalFlow.Tests.Isolation;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace GoalFlow.Tests.Mensajeria;

public sealed record FilaDeEntrega(
    Guid Id, Guid TenantId, string Consumidor, string Estado, int Intentos, Guid AgregadoId, int Version,
    Guid? Token, DateTime ProximoIntentoEn, DateTime? LeaseVenceEn, string? UltimoError, DateTime CreadoEn, DateTime? CompletadoEn);

public static class AyudantesDeMensajeria
{
    public static readonly TimeSpan TopeDeEspera = TimeSpan.FromSeconds(30);

    /// <summary>Lectura como superusuario: RLS no aplica, y así se ve lo que hay de verdad en la tabla.</summary>
    public static async Task<List<FilaDeEntrega>> LeerEntregas(this BaseDeAislamiento db, params string[] consumidores)
    {
        await using var conexion = new NpgsqlConnection(db.CadenaSuperusuario);
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand("""
            select id, tenant_id, consumidor, estado, intentos, agregado_id, version_del_agregado,
                   token_del_lease, proximo_intento_en, lease_vence_en, ultimo_error, creado_en, completado_en
            from goalflow.entregas_eventos
            where consumidor = any(@consumidores)
            order by creado_en, consumidor
            """, conexion);
        comando.Parameters.AddWithValue("consumidores", consumidores);
        await using var lector = await comando.ExecuteReaderAsync();

        var filas = new List<FilaDeEntrega>();
        while (await lector.ReadAsync())
            filas.Add(new FilaDeEntrega(
                lector.GetGuid(0), lector.GetGuid(1), lector.GetString(2), lector.GetString(3), lector.GetInt32(4),
                lector.GetGuid(5), lector.GetInt32(6),
                lector.IsDBNull(7) ? null : lector.GetGuid(7), lector.GetDateTime(8),
                lector.IsDBNull(9) ? null : lector.GetDateTime(9),
                lector.IsDBNull(10) ? null : lector.GetString(10), lector.GetDateTime(11),
                lector.IsDBNull(12) ? null : lector.GetDateTime(12)));
        return filas;
    }

    /// <summary>Inserta una entrega ya disponible, sin pasar por la aplicación (por ejemplo, de un consumidor no registrado).</summary>
    public static async Task<Guid> InsertarEntrega(
        this BaseDeAislamiento db, TenantId tenant, string consumidor, Guid? agregado = null, int version = 1)
    {
        var id = Guid.NewGuid();
        await using var conexion = new NpgsqlConnection(db.CadenaSuperusuario);
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand("""
            insert into goalflow.entregas_eventos
              (id, tenant_id, evento_id, tipo_de_evento, agregado_id, version_del_agregado, consumidor, estado, intentos)
            values (@id, @tenant, @evento, @tipo, @agregado, @version, @consumidor, 'Pendiente', 0)
            """, conexion);
        comando.Parameters.AddWithValue("id", id);
        comando.Parameters.AddWithValue("tenant", tenant.Value);
        comando.Parameters.AddWithValue("evento", Guid.NewGuid());
        comando.Parameters.AddWithValue("tipo", EventoDePrueba.NombreDelEvento);
        comando.Parameters.AddWithValue("agregado", agregado ?? Guid.NewGuid());
        comando.Parameters.AddWithValue("version", version);
        comando.Parameters.AddWithValue("consumidor", consumidor);
        await comando.ExecuteNonQueryAsync();
        return id;
    }

    /// <summary>Sesiones esperando un bloqueo de fila: prueba que una sentencia quedó detenida a la espera de otra transacción.</summary>
    public static async Task<long> ContarBloqueados(this BaseDeAislamiento db)
    {
        await using var conexion = new NpgsqlConnection(db.CadenaSuperusuario);
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand(
            "select count(*) from pg_stat_activity where wait_event_type = 'Lock' and datname = current_database()", conexion);
        return (long)(await comando.ExecuteScalarAsync())!;
    }

    /// <summary>Sondeo con tope de 30 s: nunca un retraso fijo como aserción (Spec 004 §4).</summary>
    public static async Task Esperar(Func<Task<bool>> condicion, string descripcion, TimeSpan? tope = null)
    {
        var limite = DateTime.UtcNow + (tope ?? TopeDeEspera);
        while (!await condicion())
        {
            if (DateTime.UtcNow > limite) throw new TimeoutException($"No ocurrió a tiempo: {descripcion}");
            await Task.Delay(25);
        }
    }

    public static Task Esperar(Func<bool> condicion, string descripcion, TimeSpan? tope = null) =>
        Esperar(() => Task.FromResult(condicion()), descripcion, tope);

    public static AsyncServiceScope ScopeDe(this IServiceProvider servicios, TenantId tenant)
    {
        var scope = servicios.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().Establecer(tenant);
        return scope;
    }

    public static Task<ITransaccion> Iniciar(this IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IUnidadDeTrabajo>().IniciarAsync(CancellationToken.None);

    public static GoalFlowDbContext Contexto(this IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<GoalFlowDbContext>();

    /// <summary>
    /// Un productor de prueba: guarda un Equipo (la tabla con tenant que existe, en lugar del futuro partido)
    /// y publica el evento, todo en una transacción que confirma o se descarta.
    /// </summary>
    public static async Task ProducirAsync(
        IServiceProvider servicios, TenantId tenant, Guid agregado, int version, string? equipo, bool confirmar)
    {
        await using var scope = servicios.ScopeDe(tenant);
        await using var transaccion = await scope.Iniciar();
        var contexto = scope.Contexto();
        if (equipo is not null) contexto.Equipos.Add(new Equipo(Guid.NewGuid(), default, "EQ-" + Guid.NewGuid().ToString("N")[..8], equipo));
        scope.ServiceProvider.GetRequiredService<IPublicadorDeEventos>().Publicar(new EventoDePrueba(tenant, agregado, version));
        await contexto.SaveChangesAsync();
        if (confirmar) await transaccion.ConfirmarAsync(CancellationToken.None);
    }

    public static string NombreUnico(string prefijo) => $"{prefijo}-{Guid.NewGuid():N}";
}

/// <summary>Cuenta SaveChanges, inicios de transacción y sentencias a entregas_eventos.</summary>
public sealed class ContadorDeActividad : SaveChangesInterceptor
{
    private int _guardados;

    public int Guardados => Volatile.Read(ref _guardados);

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _guardados);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> SavingChanges(
        Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData, InterceptionResult<int> result)
    {
        Interlocked.Increment(ref _guardados);
        return result;
    }
}

public sealed class ContadorDeTransacciones : DbTransactionInterceptor
{
    private int _inicios;

    public int Inicios => Volatile.Read(ref _inicios);

    public override ValueTask<InterceptionResult<System.Data.Common.DbTransaction>> TransactionStartingAsync(
        System.Data.Common.DbConnection connection, TransactionStartingEventData eventData,
        InterceptionResult<System.Data.Common.DbTransaction> result, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _inicios);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<System.Data.Common.DbTransaction> TransactionStarting(
        System.Data.Common.DbConnection connection, TransactionStartingEventData eventData,
        InterceptionResult<System.Data.Common.DbTransaction> result)
    {
        Interlocked.Increment(ref _inicios);
        return result;
    }
}

/// <summary>Captura todo lo que se registra, con su nivel, y la excepción completa: si el mensaje de una excepción se filtra, aquí se ve (T-23).</summary>
public sealed class CapturaDeRegistros : ILoggerProvider
{
    public sealed record Entrada(LogLevel Nivel, string Categoria, string Texto);

    private readonly ConcurrentQueue<Entrada> _entradas = new();

    public IReadOnlyList<Entrada> Entradas => [.. _entradas];

    public ILogger CreateLogger(string categoryName) => new Registrador(categoryName, _entradas);

    public void Dispose()
    {
    }

    private sealed class Registrador(string categoria, ConcurrentQueue<Entrada> entradas) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var estado = state is IEnumerable<KeyValuePair<string, object?>> pares
                ? string.Join(" ", pares.Select(p => $"{p.Key}={p.Value}"))
                : string.Empty;
            entradas.Enqueue(new Entrada(logLevel, categoria, $"{formatter(state, exception)} {estado} {exception}"));
        }
    }
}
