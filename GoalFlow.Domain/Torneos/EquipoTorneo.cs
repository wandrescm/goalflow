using GoalFlow.Domain.Tenancy;

namespace GoalFlow.Domain.Torneos;

/// <summary>Participación de un equipo en un torneo (ADR-06 §2.1).</summary>
public class EquipoTorneo(Guid id, TenantId tenantId, Guid torneoId, Guid equipoId)
{
    public Guid Id { get; private set; } = id;
    public TenantId TenantId { get; private set; } = tenantId;
    public Guid TorneoId { get; private set; } = torneoId;
    public Guid EquipoId { get; private set; } = equipoId;
    public EstadoDeParticipacion Estado { get; private set; } = EstadoDeParticipacion.Inscrito;

    /// <returns><c>true</c> si el estado cambió.</returns>
    public bool Retirar() => false;

    /// <returns><c>true</c> si el estado cambió.</returns>
    public bool Reinscribir() => false;
}
