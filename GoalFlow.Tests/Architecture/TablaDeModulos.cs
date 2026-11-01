namespace GoalFlow.Tests.Architecture;

/// <summary>Tabla de ADR-03 §7 como datos. Cada fila nueva exige actualizar el ADR.</summary>
public static class TablaDeModulos
{
    public static readonly IReadOnlyDictionary<string, IReadOnlyCollection<string>> Adr03 =
        new Dictionary<string, IReadOnlyCollection<string>>
        {
            ["Tenancy"] = [],
            ["Ligas"] = ["Tenancy"],
            ["Torneos"] = ["Tenancy"],
            ["Estadisticas"] = ["Tenancy", "Torneos.Contracts"],
            ["Pollas"] = ["Tenancy", "Torneos.Contracts", "Estadisticas.Contracts"],
            ["IA"] = ["Tenancy", "Torneos.Contracts", "Estadisticas.Contracts"],
        };

    /// <summary>Namespaces de capa que no son módulos: composición (Hosting) y persistencia compartida (ADR-14 §2.3).</summary>
    public static readonly IReadOnlyCollection<string> NamespacesCompartidos = ["Hosting", "Persistencia"];

    public static readonly IReadOnlyCollection<string> RaicesDeCapa =
    [
        ReglasDeArquitectura.NombreDomain,
        ReglasDeArquitectura.NombreApplication,
        ReglasDeArquitectura.NombreInfrastructure,
        ReglasDeArquitectura.NombreApi,
    ];
}
