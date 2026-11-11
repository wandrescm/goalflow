namespace GoalFlow.Application.IA;

/// <summary>
/// Contador de gasto de IA de la liga del scope (Spec 007 R-03, R-19). Toda operación exige una transacción activa y lanza
/// <see cref="GoalFlow.Application.Persistencia.TransaccionRequeridaException"/> si no la hay (R-26): sin ella, RLS dejaría
/// cero filas y se vería igual que perder la carrera.
/// </summary>
public interface IAlmacenDePresupuesto
{
    /// <summary>
    /// Reserva <paramref name="monto"/> (µ$) en el contador del mes con un compare-and-swap:
    /// <c>reservado + consumido + monto &lt;= tope</c>. Devuelve false si no afectó exactamente una fila (sin contador, o sin saldo).
    /// </summary>
    Task<bool> ReservarAsync(string mes, long monto, CancellationToken cancellationToken);
}
