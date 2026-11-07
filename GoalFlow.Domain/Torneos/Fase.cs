using GoalFlow.Domain.Tenancy;

namespace GoalFlow.Domain.Torneos;

public class Fase(Guid id, TenantId tenantId, Guid torneoId, int orden, TipoDeFase tipo, int vueltas)
{
    public Guid Id { get; private set; } = id;
    public TenantId TenantId { get; private set; } = tenantId;
    public Guid TorneoId { get; private set; } = torneoId;
    public int Orden { get; private set; } = orden;
    public TipoDeFase Tipo { get; private set; } = tipo;
    public int Vueltas { get; private set; } = vueltas;

    /// <returns><c>true</c> si el tipo o las vueltas cambiaron.</returns>
    public bool Configurar(TipoDeFase tipo, int vueltas) => false;
}
