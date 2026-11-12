using GoalFlow.Application.Torneos.Contracts;

namespace GoalFlow.Application.IA;

/// <summary>Lo que A-01 produce para una planilla (Spec 007 R-10, ADR-12 §2.2).</summary>
public enum ClasificacionDeResumen
{
    /// <summary>Nada: ni resumen ni llamada.</summary>
    Ninguno,

    /// <summary>Un texto determinista, sin presupuesto ni llamada.</summary>
    Plantilla,

    /// <summary>Una llamada al modelo.</summary>
    Narrativa,
}

public static class ClasificadorDeResumen
{
    /// <exception cref="ArgumentOutOfRangeException">Un estado que IA no conoce: no se clasifica en silencio.</exception>
    public static ClasificacionDeResumen Clasificar(DatosDePartidoParaResumen datos) => datos.Estado switch
    {
        CodigosDeTorneos.Programado or CodigosDeTorneos.Suspendido or CodigosDeTorneos.Cancelado => ClasificacionDeResumen.Ninguno,
        CodigosDeTorneos.Adjudicado => ClasificacionDeResumen.Plantilla,

        // Un evento con nombre: un gol de jugador, un autogol o una tarjeta (los goles sin autor no lo son).
        CodigosDeTorneos.Jugado => datos.Goles.Count > 0 || datos.Autogoles.Count > 0 || datos.Tarjetas.Count > 0
            ? ClasificacionDeResumen.Narrativa
            : ClasificacionDeResumen.Plantilla,

        _ => throw new ArgumentOutOfRangeException(nameof(datos), "Estado de partido desconocido para IA."),
    };
}
