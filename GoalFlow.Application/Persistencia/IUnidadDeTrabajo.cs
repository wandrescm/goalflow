namespace GoalFlow.Application.Persistencia;

/// <summary>Abre la transacción explícita de toda unidad de trabajo (ADR-14 §2.3).</summary>
public interface IUnidadDeTrabajo
{
    /// <exception cref="ContextoDeTransaccionAusenteException">Sin tenant ni identidad.</exception>
    Task<ITransaccion> IniciarAsync(CancellationToken cancellationToken);
}

/// <summary>Transacción abierta. Si se libera sin confirmar, se revierte.</summary>
public interface ITransaccion : IAsyncDisposable
{
    Task ConfirmarAsync(CancellationToken cancellationToken);
}
