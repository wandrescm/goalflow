using GoalFlow.Domain.Tenancy;

namespace GoalFlow.Infrastructure.Mensajeria;

public enum EstadoDeEntrega
{
    Pendiente,
    EnProceso,
    Completada,
    Fallida,
}

/// <summary>
/// Una fila por consumidor registrado (ADR-02 §2.3). Las transiciones de estado las hacen las operaciones
/// de <c>ExecuteUpdateAsync</c>, no esta clase; por eso solo se construye al publicar.
/// </summary>
public class EntregaEvento
{
    private EntregaEvento()
    {
    }

    public Guid Id { get; private set; }

    public TenantId TenantId { get; private set; }

    /// <summary>Solo para trazar: todas las filas de una publicación lo comparten.</summary>
    public Guid EventoId { get; private set; }

    public string TipoDeEvento { get; private set; } = string.Empty;

    public Guid AgregadoId { get; private set; }

    public int VersionDelAgregado { get; private set; }

    public string Consumidor { get; private set; } = string.Empty;

    public EstadoDeEntrega Estado { get; private set; }

    public int Intentos { get; private set; }

    /// <summary>La pone la base (default now()); la aplicación nunca envía un timestamp (R-26).</summary>
    public DateTime ProximoIntentoEn { get; private set; }

    public Guid? TokenDelLease { get; private set; }

    public DateTime? LeaseVenceEn { get; private set; }

    /// <summary>Solo el tipo de excepción o el código de razón; nunca un mensaje (R-14).</summary>
    public string? UltimoError { get; private set; }

    public DateTime CreadoEn { get; private set; }

    public DateTime? CompletadoEn { get; private set; }

    public static EntregaEvento Nueva(
        TenantId tenantId, Guid eventoId, string tipoDeEvento, Guid agregadoId, int version, string consumidor) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EventoId = eventoId,
            TipoDeEvento = tipoDeEvento,
            AgregadoId = agregadoId,
            VersionDelAgregado = version,
            Consumidor = consumidor,
            Estado = EstadoDeEntrega.Pendiente,
        };
}
