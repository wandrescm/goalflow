using GoalFlow.Domain.Tenancy;

namespace GoalFlow.Application.Tenancy;

/// <summary>Tenant del scope actual (ADR-14 §2.4). Se fija una vez por scope.</summary>
public interface ITenantContext
{
    bool TieneTenant { get; }

    /// <exception cref="TenantAusenteException">Si todavía no se fijó el tenant.</exception>
    TenantId Current { get; }

    /// <exception cref="ArgumentException">Si el tenant está vacío.</exception>
    /// <exception cref="InvalidOperationException">Si ya se fijó otro tenant en este scope.</exception>
    void Establecer(TenantId tenant);
}
