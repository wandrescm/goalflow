using GoalFlow.Domain.Tenancy;

namespace GoalFlow.Domain.Torneos;

/// <summary>Jugador de la liga (identidad estable entre torneos). Solo código y nombre (Spec 005 §4).</summary>
public class Jugador(Guid id, TenantId tenantId, string codigo, string nombre)
{
    public Guid Id { get; private set; } = id;
    public TenantId TenantId { get; private set; } = tenantId;
    public string Codigo { get; private set; } = codigo;
    public string Nombre { get; private set; } = nombre;

    /// <returns><c>true</c> si el nombre cambió.</returns>
    public bool Renombrar(string nombre) => false;
}
