using GoalFlow.Application.Tenancy;
using GoalFlow.Domain.Tenancy;

namespace GoalFlow.Infrastructure.Persistencia;

/// <summary>Adaptador scoped de ITenantContext. Vive aquí porque Tenancy no crece (Spec 002 R-01).</summary>
public class ContextoDeTenant : ITenantContext
{
    private TenantId? _tenant;

    public bool TieneTenant => _tenant is not null;

    public TenantId Current => _tenant ?? throw new TenantAusenteException();

    public void Establecer(TenantId tenant)
    {
        if (tenant.EsVacio)
            throw new ArgumentException("El tenant no puede estar vacío.", nameof(tenant));
        if (_tenant is { } actual && actual != tenant)
            throw new InvalidOperationException("Este scope ya tiene otro tenant.");

        _tenant = tenant;
    }
}
