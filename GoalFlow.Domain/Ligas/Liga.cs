using GoalFlow.Domain.Tenancy;

namespace GoalFlow.Domain.Ligas;

/// <summary>Catálogo de ligas. Tabla global: la API solo la lee (ADR-14 §2.7).</summary>
public class Liga(TenantId id, string slug, string nombre)
{
    public TenantId Id { get; private set; } = id;
    public string Slug { get; private set; } = slug;
    public string Nombre { get; private set; } = nombre;
}
