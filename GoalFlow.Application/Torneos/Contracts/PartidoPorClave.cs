namespace GoalFlow.Application.Torneos.Contracts;

/// <summary>
/// La clave natural de un partido, la misma de la ruta de la planilla del Admin (Spec 006 R-08): código del torneo, orden de la fase y
/// códigos del equipo local y del visitante. Los códigos se comparan exactamente, sensibles a mayúsculas (Spec 011 R-08, C-07).
/// </summary>
public sealed record ClaveNaturalDePartido(string CodigoTorneo, int OrdenFase, string CodigoLocal, string CodigoVisitante);

/// <summary>
/// Un partido leído por su clave natural (Spec 011 R-30): su identificador, que sirve para leer sus resúmenes (R-15) y nunca se
/// serializa, y los mismos datos que <see cref="ILectorDePartidoParaResumen.LeerAsync"/> devuelve para ese partido.
/// </summary>
public sealed record PartidoPorClave(Guid PartidoId, DatosDePartidoParaResumen Datos);
