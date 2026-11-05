namespace GoalFlow.Application.Mensajeria;

/// <summary>
/// Consumidor de un tipo de evento. Su <see cref="Nombre"/> es estable y constante: identifica sus filas
/// de entrega (Spec 004 R-03). Se entrega al menos una vez, así que debe tolerar la reentrega (ADR-02 §2.7).
/// </summary>
public interface IConsumidorDeEvento<TEvento> where TEvento : IEventoDeIntegracion
{
    /// <summary>Estático para poder validarlo al arrancar sin crear el consumidor, que es scoped.</summary>
    static abstract string Nombre { get; }

    Task ProcesarAsync(EntregaDeEvento<TEvento> entrega, CancellationToken cancellationToken);
}

/// <summary>El evento reconstruido desde la fila y el número de intento (1 en la primera entrega).</summary>
public sealed record EntregaDeEvento<TEvento>(TEvento Evento, int Intento) where TEvento : IEventoDeIntegracion;
