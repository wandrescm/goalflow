using GoalFlow.Domain.Tenancy;

namespace GoalFlow.Domain.Torneos;

/// <summary>Inscripción de un jugador con un equipo participante de un torneo. Una por jugador y torneo.</summary>
public class InscripcionJugador(Guid id, TenantId tenantId, Guid torneoId, Guid equipoTorneoId, Guid jugadorId)
{
    public Guid Id { get; private set; } = id;
    public TenantId TenantId { get; private set; } = tenantId;
    public Guid TorneoId { get; private set; } = torneoId;
    public Guid EquipoTorneoId { get; private set; } = equipoTorneoId;
    public Guid JugadorId { get; private set; } = jugadorId;
}
