namespace GoalFlow.Application.Persistencia;

/// <summary>Abre la transacción explícita de toda unidad de trabajo (ADR-14 §2.3).</summary>
public interface IUnidadDeTrabajo
{
    /// <exception cref="ContextoDeTransaccionAusenteException">Sin tenant ni identidad.</exception>
    Task<ITransaccion> IniciarAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Transacción de solo lectura de hecho con aislamiento <c>REPEATABLE READ</c> (Spec 009 R-11, R-33): todo lo que se lee
    /// dentro de ella sale de una sola foto de la base. El interceptor del tenant aplica igual.
    /// </summary>
    /// <exception cref="ContextoDeTransaccionAusenteException">Sin tenant ni identidad.</exception>
    Task<ITransaccion> IniciarLecturaConsistenteAsync(CancellationToken cancellationToken);
}

/// <summary>Transacción abierta. Si se libera sin confirmar, se revierte.</summary>
public interface ITransaccion : IAsyncDisposable
{
    Task ConfirmarAsync(CancellationToken cancellationToken);
}
