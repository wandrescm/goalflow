using GoalFlow.Application.Mensajeria;
using GoalFlow.Domain.Tenancy;

namespace GoalFlow.Application.Torneos.Contracts;

/// <summary>
/// Se guardó una versión nueva de la planilla de un partido (Spec 006 R-23). Solo identificadores y versión, nunca datos
/// del partido: quien lo consume vuelve a leer el estado vigente (ADR-02 §2.6). <see cref="AgregadoId"/> es el partido.
/// </summary>
public sealed record PartidoGuardado(TenantId TenantId, Guid AgregadoId, int Version) : IEventoDeIntegracion
{
    public static string NombreDelEvento => "torneos.partido-guardado";
}
