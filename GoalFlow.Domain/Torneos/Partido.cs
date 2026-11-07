using GoalFlow.Domain.Tenancy;

namespace GoalFlow.Domain.Torneos;

public class Partido(
    Guid id, TenantId tenantId, Guid torneoId, Guid faseId, int jornada, Guid localId, Guid visitanteId, DateTimeOffset? programadoEn)
{
    public Guid Id { get; private set; } = id;
    public TenantId TenantId { get; private set; } = tenantId;
    public Guid TorneoId { get; private set; } = torneoId;
    public Guid FaseId { get; private set; } = faseId;
    public int Jornada { get; private set; } = jornada;
    public Guid LocalId { get; private set; } = localId;
    public Guid VisitanteId { get; private set; } = visitanteId;
    public DateTimeOffset? ProgramadoEn { get; private set; } = programadoEn;
    public EstadoDePartido Estado { get; private set; } = EstadoDePartido.Programado;

    /// <returns><c>true</c> si la jornada o la hora cambiaron.</returns>
    public bool Reprogramar(int jornada, DateTimeOffset? programadoEn) => false;

    /// <returns><c>true</c> si el estado cambió.</returns>
    public bool Cancelar() => false;

    /// <returns><c>true</c> si el estado cambió.</returns>
    public bool Reactivar() => false;
}
