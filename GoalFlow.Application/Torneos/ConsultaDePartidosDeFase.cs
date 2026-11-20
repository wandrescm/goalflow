using GoalFlow.Application.Persistencia;
using GoalFlow.Application.Tenancy;
using Microsoft.Extensions.Logging;

namespace GoalFlow.Application.Torneos;

public sealed record TorneoDeLaLista(string Codigo, string Nombre);

public sealed record FaseDeLaLista(int Orden);

/// <summary>Un equipo de la liga con su código y su nombre (Spec 011 R-04).</summary>
public sealed record EquipoDeLaLista(string Codigo, string Nombre);

/// <summary>Un marcador público: <c>marcador</c> y <c>oficial</c> de la planilla vigente (Spec 011 R-05).</summary>
public sealed record MarcadorPublico(int Local, int Visitante);

/// <summary>
/// Un partido de la lista de una fase (Spec 011 R-04). <c>Estado</c> es el guardado en <c>partidos.estado</c>; <c>Marcador</c>,
/// <c>Oficial</c> y <c>Motivo</c> salen solo de la planilla vigente. <c>ProgramadoPara</c> viaja como <c>DateTime</c> con <c>Kind</c>
/// UTC para que el JSON termine en <c>Z</c> (R-07). El orden de los miembros es el del cuerpo JSON.
/// </summary>
public sealed record PartidoDeLaLista(
    int Jornada, DateTime? ProgramadoPara, EquipoDeLaLista Local, EquipoDeLaLista Visitante, string Estado,
    MarcadorPublico? Marcador, MarcadorPublico? Oficial, string? Motivo);

public sealed record PartidosDeLaFase(TorneoDeLaLista Torneo, FaseDeLaLista Fase, IReadOnlyList<PartidoDeLaLista> Partidos);

/// <summary>El resultado de la lectura: distingue <i>encontrado</i> de <i>torneo o fase inexistente</i> (Spec 011 R-03).</summary>
public sealed record ResultadoDePartidosDeFase(bool Encontrado, PartidosDeLaFase? Partidos)
{
    public static ResultadoDePartidosDeFase Ok(PartidosDeLaFase partidos) => new(true, partidos);

    public static ResultadoDePartidosDeFase NoEncontrado() => new(false, null);
}

/// <summary>
/// Lee, dentro de la transacción de quien llama, todos los partidos de una fase del tenant actual en cualquiera de sus cinco estados,
/// en el orden de R-06. Devuelve <c>null</c> si el torneo o la fase no existen en la liga. Los marcadores salen de la planilla cuya
/// versión es <c>partidos.version</c> (Spec 006 R-07), nunca de «la versión más alta».
/// </summary>
public interface ILectorDePartidosDeFase
{
    Task<PartidosDeLaFase?> ListarAsync(string codigoDeTorneo, int ordenDeFase, CancellationToken cancellationToken);
}

/// <summary>Lectura pública de los partidos de una fase (Spec 011 R-03 a R-07): una sola foto de la base (R-20) y ninguna escritura (R-19).</summary>
public sealed class ConsultaDePartidosDeFase(
    IUnidadDeTrabajo unidadDeTrabajo, ILectorDePartidosDeFase lector, ITenantContext tenantContext, ILogger<ConsultaDePartidosDeFase> logger)
{
    public async Task<ResultadoDePartidosDeFase> EjecutarAsync(string codigoDeTorneo, int ordenDeFase, CancellationToken cancellationToken)
    {
        PartidosDeLaFase? partidos;
        await using (var transaccion = await unidadDeTrabajo.IniciarLecturaConsistenteAsync(cancellationToken))
        {
            partidos = await lector.ListarAsync(codigoDeTorneo, ordenDeFase, cancellationToken);
            await transaccion.ConfirmarAsync(cancellationToken);
        }

        // Nunca nombres de equipos (R-26).
        logger.LogInformation(
            "Lectura pública: liga {Liga}, ruta {Ruta}, torneo {Torneo}, fase {Fase}, partidos {Partidos}, encontrado {Encontrado}",
            tenantContext.Current.Value, "partidos", codigoDeTorneo, ordenDeFase, partidos?.Partidos.Count ?? 0, partidos is not null);
        return partidos is null ? ResultadoDePartidosDeFase.NoEncontrado() : ResultadoDePartidosDeFase.Ok(partidos);
    }
}
