namespace GoalFlow.Tests.Architecture;

/// <summary>Tabla de ADR-03 §7 como datos. Cada fila nueva exige actualizar el ADR.</summary>
public static class TablaDeModulos
{
    public static readonly IReadOnlyDictionary<string, IReadOnlyCollection<string>> Adr03 =
        new Dictionary<string, IReadOnlyCollection<string>>
        {
            ["Tenancy"] = [],
            ["Torneos"] = ["Tenancy"],
            ["Estadisticas"] = ["Tenancy", "Torneos.Contracts"],
            ["Pollas"] = ["Tenancy", "Torneos.Contracts", "Estadisticas.Contracts"],
            ["IA"] = ["Tenancy", "Torneos.Contracts", "Estadisticas.Contracts"],
        };

    public static readonly IReadOnlyCollection<string> RaicesDeCapa =
    [
        ReglasDeArquitectura.NombreDomain,
        ReglasDeArquitectura.NombreApplication,
        ReglasDeArquitectura.NombreInfrastructure,
        ReglasDeArquitectura.NombreApi,
    ];
}
