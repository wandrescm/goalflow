using GoalFlow.Domain.Tenancy;

namespace GoalFlow.Domain.Torneos;

/// <summary>Equipo de la liga. Su código es la clave natural estable entre importaciones (ADR-06 §2.4).</summary>
public class Equipo(Guid id, TenantId tenantId, string codigo, string nombre)
{
    // Solo para EF Core mientras Codigo no tiene columna (llega con la migración TorneosYPartidos).
    private Equipo(Guid id, TenantId tenantId, string nombre) : this(id, tenantId, string.Empty, nombre)
    {
    }

    public Guid Id { get; private set; } = id;
    public TenantId TenantId { get; private set; } = tenantId;
    public string Codigo { get; private set; } = codigo;
    public string Nombre { get; private set; } = nombre;

    /// <returns><c>true</c> si el nombre cambió.</returns>
    public bool Renombrar(string nombre) => false;
}
