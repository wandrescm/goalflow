namespace GoalFlow.Application.IA.Verificacion;

/// <summary>
/// El veredicto del verificador determinista (Spec 008 R-01): aprobado si y solo si <see cref="Reglas"/> está vacía.
/// Las reglas son los identificadores <c>V1</c> a <c>V7</c> que fallaron, sin repetir y en ese orden.
/// </summary>
public sealed record ResultadoDeVerificacion(IReadOnlyList<string> Reglas)
{
    public bool Aprobado => Reglas.Count == 0;
}
