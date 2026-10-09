namespace GoalFlow.Tests.Architecture;

public sealed record ResultadoRegla(bool Cumple, IReadOnlyList<string> Violaciones)
{
    public static ResultadoRegla Desde(IReadOnlyList<string> violaciones) =>
        new(violaciones.Count == 0, violaciones);

    public override string ToString() =>
        Cumple ? "Cumple" : "Violaciones: " + string.Join("; ", Violaciones);
}
