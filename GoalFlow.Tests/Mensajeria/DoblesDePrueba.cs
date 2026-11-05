using System.Collections.Concurrent;
using GoalFlow.Application.Mensajeria;
using GoalFlow.Domain.Tenancy;
using GoalFlow.Infrastructure.Mensajeria;
using Microsoft.Extensions.DependencyInjection;

namespace GoalFlow.Tests.Mensajeria;

// Evento y consumidores de prueba: viven solo aquí, nunca en producción (Spec 004 §4).

public sealed record EventoDePrueba(TenantId TenantId, Guid AgregadoId, int Version) : IEventoDeIntegracion
{
    public static string NombreDelEvento => "prueba.evento";
}

/// <summary>Ningún consumidor lo escucha (T-20).</summary>
public sealed record EventoSinConsumidores(TenantId TenantId, Guid AgregadoId, int Version) : IEventoDeIntegracion
{
    public static string NombreDelEvento => "prueba.sin-consumidores";
}

public sealed record LlamadaRegistrada(TenantId Tenant, Guid AgregadoId, int Version, int Intento);

public interface IRanura;

public sealed class RanuraA : IRanura;

public sealed class RanuraB : IRanura;

/// <summary>Lo que hace el consumidor de una ranura en esta prueba, y lo que recibió.</summary>
public sealed class Comportamiento<TRanura> where TRanura : IRanura
{
    private readonly ConcurrentQueue<LlamadaRegistrada> _llamadas = new();
    private int _enCurso;

    public Func<EntregaDeEvento<EventoDePrueba>, CancellationToken, Task> Accion { get; set; } = (_, _) => Task.CompletedTask;

    public IReadOnlyList<LlamadaRegistrada> Llamadas => [.. _llamadas];

    public int Corridas => _llamadas.Count;

    public int EnCurso => Volatile.Read(ref _enCurso);

    internal async Task EjecutarAsync(EntregaDeEvento<EventoDePrueba> entrega, CancellationToken cancellationToken)
    {
        _llamadas.Enqueue(new LlamadaRegistrada(
            entrega.Evento.TenantId, entrega.Evento.AgregadoId, entrega.Evento.Version, entrega.Intento));
        Interlocked.Increment(ref _enCurso);
        try
        {
            await Accion(entrega, cancellationToken);
        }
        finally
        {
            Interlocked.Decrement(ref _enCurso);
        }
    }
}

public sealed class ConsumidorDePrueba<TRanura>(Comportamiento<TRanura> comportamiento)
    : IConsumidorDeEvento<EventoDePrueba> where TRanura : IRanura
{
    public static string Nombre => "prueba-" + typeof(TRanura).Name;

    public Task ProcesarAsync(EntregaDeEvento<EventoDePrueba> entrega, CancellationToken cancellationToken) =>
        comportamiento.EjecutarAsync(entrega, cancellationToken);
}

/// <summary>Un consumidor registrado con un nombre único: el contenedor es compartido y goalflow_app no borra (handoff §4).</summary>
public sealed record ConsumidorRegistrado<TRanura>(string Nombre, Comportamiento<TRanura> Comportamiento) where TRanura : IRanura;

public static class RegistroDePruebas
{
    public static ConsumidorRegistrado<TRanura> AddConsumidorDePrueba<TRanura>(this IServiceCollection servicios)
        where TRanura : IRanura
    {
        var nombre = $"{ConsumidorDePrueba<TRanura>.Nombre}-{Guid.NewGuid():N}";
        var comportamiento = new Comportamiento<TRanura>();
        servicios.AddSingleton(comportamiento);
        servicios.AddConsumidorDeEvento<EventoDePrueba, ConsumidorDePrueba<TRanura>>(nombre);
        return new ConsumidorRegistrado<TRanura>(nombre, comportamiento);
    }
}
