using GoalFlow.Domain.Tenancy;

namespace GoalFlow.Domain.Torneos;

/// <summary>Forma mínima para probar el aislamiento; la extiende la Spec del modelo de dominio.</summary>
public class Equipo(Guid id, TenantId tenantId, string nombre)
{
    public Guid Id { get; private set; } = id;
    public TenantId TenantId { get; private set; } = tenantId;
    public string Nombre { get; private set; } = nombre;
}
