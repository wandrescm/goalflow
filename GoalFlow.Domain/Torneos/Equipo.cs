using GoalFlow.Domain.Tenancy;

namespace GoalFlow.Domain.Torneos;

/// <summary>Equipo de la liga. Su código es la clave natural estable entre importaciones (ADR-06 §2.4).</summary>
public class Equipo
{
    public Equipo(Guid id, TenantId tenantId, string codigo, string nombre)
    {
        Id = id;
        TenantId = tenantId;
        Codigo = Reglas.ExigirCodigo(codigo);
        Nombre = Reglas.ExigirNombre(nombre);
    }

    // Solo para EF Core mientras Codigo no tiene columna (llega con la migración TorneosYPartidos).
    private Equipo(Guid id, TenantId tenantId, string nombre)
    {
        Id = id;
        TenantId = tenantId;
        Codigo = string.Empty;
        Nombre = nombre;
    }

    public Guid Id { get; private set; }
    public TenantId TenantId { get; private set; }
    public string Codigo { get; private set; }
    public string Nombre { get; private set; }

    /// <returns><c>true</c> si el nombre cambió.</returns>
    public bool Renombrar(string nombre)
    {
        var nuevo = Reglas.ExigirNombre(nombre);
        if (nuevo == Nombre) return false;
        Nombre = nuevo;
        return true;
    }
}
