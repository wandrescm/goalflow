namespace GoalFlow.Domain.Torneos;

// Descripción de un fixture completo: el estado deseado de un torneo (Spec 005 R-14). Valores primitivos: la
// capa de entrada los traduce y el validador los revisa sin tocar la base.

public sealed record JugadorDescripcion(string Codigo, string Nombre);

public sealed record EquipoDescripcion(string Codigo, string Nombre, bool Retirado, IReadOnlyList<JugadorDescripcion> Jugadores);

public sealed record FaseDescripcion(int Orden, string Tipo, int Vueltas);

/// <param name="ProgramadoEn">Texto ISO 8601 con desplazamiento explícito; el validador lo comprueba (R-17).</param>
public sealed record PartidoDescripcion(int Fase, int Jornada, string Local, string Visitante, string? ProgramadoEn, bool Cancelado);

public sealed record FixtureDescripcion(
    string Nombre,
    IReadOnlyList<FaseDescripcion> Fases,
    IReadOnlyList<EquipoDescripcion> Equipos,
    IReadOnlyList<PartidoDescripcion> Partidos);

/// <param name="Ruta">Ruta en la petición, por ejemplo <c>partidos[3].visitante</c>.</param>
/// <param name="Codigo">Código estable del error; nunca contiene datos del usuario.</param>
public sealed record ErrorDeFixture(string Ruta, string Codigo);
