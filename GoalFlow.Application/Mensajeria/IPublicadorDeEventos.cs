namespace GoalFlow.Application.Mensajeria;

/// <summary>
/// Publica un evento escribiendo una fila de entrega por consumidor registrado (ADR-02 §2.3).
/// No guarda: el SaveChanges del productor escribe las filas en su propia transacción (Spec 004 R-05).
/// </summary>
public interface IPublicadorDeEventos
{
    /// <exception cref="GoalFlow.Application.Persistencia.TransaccionRequeridaException">Sin transacción activa (R-06).</exception>
    void Publicar<TEvento>(TEvento evento) where TEvento : IEventoDeIntegracion;

    /// <summary>
    /// Publicación dirigida (Spec 007 R-31): agrega <b>una</b> fila de entrega, la del consumidor nombrado, si ese consumidor está
    /// registrado para este tipo de evento en este host. No hace fan-out. Igual que <see cref="Publicar{TEvento}"/>, no guarda: el
    /// SaveChanges del productor escribe la fila, el aviso al ciclo sale tras el commit y un duplicado sale de ese SaveChanges como
    /// <see cref="EntregaDuplicadaException"/> con sus claves pendientes (Spec 006 R-20).
    /// </summary>
    /// <exception cref="ConsumidorNoRegistradoException">El consumidor no está registrado para este evento en este host.</exception>
    /// <exception cref="GoalFlow.Application.Persistencia.TransaccionRequeridaException">Sin transacción activa.</exception>
    void PublicarPara<TEvento>(TEvento evento, string consumidor) where TEvento : IEventoDeIntegracion;
}
