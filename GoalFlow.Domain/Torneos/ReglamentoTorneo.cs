using GoalFlow.Domain.Tenancy;

namespace GoalFlow.Domain.Torneos;

/// <summary>
/// Una versión del reglamento de un torneo (Spec 009 R-01). Inmutable: un cambio es una versión nueva, y la base no concede
/// UPDATE ni DELETE sobre la tabla (R-02). <see cref="GuardadoEn"/> lo pone el reloj de la base. El reglamento vigente es
/// la versión más alta (R-03).
/// </summary>
public class ReglamentoTorneo(
    Guid id, TenantId tenantId, Guid torneoId, int version, EstadoDeReglamento estado, ContenidoDeReglamento contenido, Guid guardadoPor)
{
    public Guid Id { get; private set; } = id;
    public TenantId TenantId { get; private set; } = tenantId;
    public Guid TorneoId { get; private set; } = torneoId;
    public int Version { get; private set; } = version >= 1 ? version : throw new ArgumentOutOfRangeException(nameof(version));
    public EstadoDeReglamento Estado { get; private set; } = estado;
    public ContenidoDeReglamento Contenido { get; private set; } = contenido;

    /// <summary>El <c>sub</c> del Admin autenticado: identificador interno, nunca se devuelve ni se registra (R-10).</summary>
    public Guid GuardadoPor { get; private set; } = guardadoPor;

    public DateTimeOffset GuardadoEn { get; private set; }

    /// <summary>Comparación canónica de R-07 (c): el estado y el contenido; el orden de los criterios importa.</summary>
    public bool Equivale(EstadoDeReglamento otroEstado, ContenidoDeReglamento otroContenido) =>
        Estado == otroEstado && Contenido == otroContenido;
}
