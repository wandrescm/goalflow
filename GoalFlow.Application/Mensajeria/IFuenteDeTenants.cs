using GoalFlow.Domain.Tenancy;

namespace GoalFlow.Application.Mensajeria;

/// <summary>Identificadores de todas las ligas. Lectura sin transacción ni contexto (ADR-14 §2.3, segunda excepción).</summary>
public interface IFuenteDeTenants
{
    Task<IReadOnlyList<TenantId>> ListarAsync(CancellationToken cancellationToken);
}
