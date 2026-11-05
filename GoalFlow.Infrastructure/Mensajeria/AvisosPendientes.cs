using GoalFlow.Domain.Tenancy;

namespace GoalFlow.Infrastructure.Mensajeria;

/// <summary>Tenants con entregas publicadas en la transacción en curso. Scoped: lo llena el publicador y lo vacía el interceptor.</summary>
public sealed class AvisosPendientes
{
    private readonly List<TenantId> _tenants = [];

    public void Anotar(TenantId tenant) => _tenants.Add(tenant);

    /// <summary>Entrega los tenants distintos anotados y vacía la lista.</summary>
    public IReadOnlyList<TenantId> Tomar()
    {
        var distintos = _tenants.Distinct().ToList();
        _tenants.Clear();
        return distintos;
    }

    public void Descartar() => _tenants.Clear();
}
