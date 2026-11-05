namespace GoalFlow.Application.Mensajeria;

/// <summary>
/// Publica un evento escribiendo una fila de entrega por consumidor registrado (ADR-02 §2.3).
/// No guarda: el SaveChanges del productor escribe las filas en su propia transacción (Spec 004 R-05).
/// </summary>
public interface IPublicadorDeEventos
{
    /// <exception cref="GoalFlow.Application.Persistencia.TransaccionRequeridaException">Sin transacción activa (R-06).</exception>
    void Publicar<TEvento>(TEvento evento) where TEvento : IEventoDeIntegracion;
}
