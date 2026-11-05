using GoalFlow.Domain.Tenancy;

namespace GoalFlow.Application.Mensajeria;

/// <summary>
/// Evento entre módulos (ADR-02, ADR-03 §6): solo identificadores y versión, nunca datos del partido.
/// Los tipos reales viven en el sub-namespace <c>.Contracts</c> del módulo emisor (Spec 004 R-02, R-39).
/// </summary>
public interface IEventoDeIntegracion
{
    /// <summary>Nombre estable y constante: las filas sobreviven a un cambio de nombre de la clase (Spec 004 C-03).</summary>
    static abstract string NombreDelEvento { get; }

    TenantId TenantId { get; }

    Guid AgregadoId { get; }

    int Version { get; }
}
