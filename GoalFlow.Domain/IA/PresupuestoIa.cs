using GoalFlow.Domain.Tenancy;

namespace GoalFlow.Domain.IA;

/// <summary>
/// El contador de gasto de una liga en un mes (Spec 007 R-03). El tope se copia de la configuración al crear la fila.
/// <c>Reservado</c> y <c>Consumido</c> los modifican solo operaciones condicionadas de <c>ExecuteUpdateAsync</c>.
/// </summary>
public class PresupuestoIa(Guid id, TenantId tenantId, string mes, long tope)
{
    public Guid Id { get; private set; } = id;
    public TenantId TenantId { get; private set; } = tenantId;
    public string Mes { get; private set; } = mes;
    public long Tope { get; private set; } = tope;
    public long Reservado { get; private set; }
    public long Consumido { get; private set; }
}
