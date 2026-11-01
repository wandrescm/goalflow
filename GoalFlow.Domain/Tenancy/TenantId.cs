namespace GoalFlow.Domain.Tenancy;

/// <summary>Identificador de un tenant. Un tenant es una liga (ADR-14 §2.1).</summary>
public readonly record struct TenantId(Guid Value)
{
    public bool EsVacio => Value == Guid.Empty;
}
