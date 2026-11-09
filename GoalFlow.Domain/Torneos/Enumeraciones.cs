namespace GoalFlow.Domain.Torneos;

/// <summary>Tipos de fase. En este hito solo existe el todos contra todos (ADR-06 §2.2).</summary>
public enum TipoDeFase
{
    TodosContraTodos,
}

public enum EstadoDeParticipacion
{
    Inscrito,
    Retirado,
}

/// <summary>Los cinco estados de un partido (Spec 006 R-01).</summary>
public enum EstadoDePartido
{
    Programado,
    Cancelado,
    Jugado,
    Suspendido,
    Adjudicado,
}
