namespace GoalFlow.Application.Estadisticas.Calculo;

// Resultados del cálculo puro (Spec 009 §3.4 a §3.5). Solo datos: ningún identificador interno y ninguna lógica.

public sealed record EquipoDeEstadisticas(string Codigo, string Nombre);

public sealed record JugadorDeEstadisticas(string Codigo, string Nombre);

/// <summary>Una fila de las posiciones de una fase (R-16). <c>Puntos</c> = <c>PuntosPorResultado</c> − <c>Deducciones</c> y puede ser negativo.</summary>
public sealed record FilaDePosicion(
    int Posicion,
    EquipoDeEstadisticas Equipo,
    int Pj,
    int Pg,
    int Pe,
    int Pp,
    int Gf,
    int Gc,
    int Dg,
    int PuntosPorResultado,
    int Deducciones,
    int Puntos,
    int PartidosConTarjetasComputables,
    int PartidosConTarjetasSinInformar);

public sealed record EquipoSinDatosDeTarjetas(string Codigo, int PartidosSinInformar);

public sealed record CriterioOmitido(string Criterio, string Motivo, IReadOnlyList<EquipoSinDatosDeTarjetas> Equipos);

/// <summary>Un empate que los criterios no resolvieron (R-20).</summary>
public sealed record EmpateSinResolver(
    int Posicion, IReadOnlyList<string> Equipos, string Motivo, IReadOnlyList<CriterioOmitido> CriteriosOmitidos);

public sealed record CoberturaDeTarjetas(int PartidosConTarjetasComputables, int PartidosConTarjetasSinInformar);

public sealed record ResultadoDePosiciones(
    IReadOnlyList<FilaDePosicion> Filas, IReadOnlyList<EmpateSinResolver> Empates, CoberturaDeTarjetas Cobertura);

public sealed record FilaDeGoleador(int Posicion, JugadorDeEstadisticas Jugador, EquipoDeEstadisticas Equipo, int Goles);

public sealed record CoberturaDeGoleadoresPorEquipo(
    EquipoDeEstadisticas Equipo, int PartidosComputables, int PartidosConGolesSinAutor, int GolesSinAutor);

/// <summary>Cobertura de los goleadores (R-23). Invariante: <c>GolesTotales</c> = <c>GolesDeJugador</c> + <c>Autogoles</c> + <c>GolesSinAutor</c>.</summary>
public sealed record CoberturaDeGoleadores(
    int PartidosComputables,
    int PartidosConGolesSinAutor,
    int GolesTotales,
    int GolesDeJugador,
    int Autogoles,
    int GolesSinAutor,
    int GolesConAutor,
    IReadOnlyList<CoberturaDeGoleadoresPorEquipo> PorEquipo);

public sealed record ResultadoDeGoleadores(IReadOnlyList<FilaDeGoleador> Filas, CoberturaDeGoleadores Cobertura);

public sealed record FilaDeValla(int Posicion, EquipoDeEstadisticas Equipo, int PartidosComputados, int GolesEnContra);

public sealed record ResultadoDeValla(IReadOnlyList<FilaDeValla> Filas);
