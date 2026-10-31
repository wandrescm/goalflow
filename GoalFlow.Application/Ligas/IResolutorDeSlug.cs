using GoalFlow.Domain.Ligas;

namespace GoalFlow.Application.Ligas;

/// <summary>Única lectura fuera de la unidad de trabajo: sin transacción ni contexto (ADR-14 §2.3).</summary>
public interface IResolutorDeSlug
{
    Task<Liga?> ResolverAsync(string slug, CancellationToken cancellationToken);
}
