using GoalFlow.Domain.Tenancy;

namespace GoalFlow.Domain.Ligas;

/// <summary>Membresía de un Admin en una liga. Tabla global: la API solo la lee (ADR-14 §2.7).</summary>
public class AdminLiga(Guid usuarioId, TenantId tenantId, string rol)
{
    public Guid UsuarioId { get; private set; } = usuarioId;
    public TenantId TenantId { get; private set; } = tenantId;
    public string Rol { get; private set; } = rol;
}
