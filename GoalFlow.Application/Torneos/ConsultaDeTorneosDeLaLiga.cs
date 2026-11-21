using GoalFlow.Application.Persistencia;
using GoalFlow.Application.Tenancy;
using Microsoft.Extensions.Logging;

namespace GoalFlow.Application.Torneos;

/// <summary>Una fase del catálogo público (Spec 011 R-01). El orden de los miembros es el del cuerpo JSON.</summary>
public sealed record FaseDelCatalogo(int Orden, string Tipo, int Vueltas);

/// <summary>Un torneo del catálogo público con sus fases por <c>orden</c> ascendente (Spec 011 R-01).</summary>
public sealed record TorneoDelCatalogo(string Codigo, string Nombre, IReadOnlyList<FaseDelCatalogo> Fases);

/// <summary>
/// Lee los torneos del tenant actual con sus fases, dentro de la transacción de quien llama (Spec 011 R-28). Los devuelve ordenados
/// por <c>codigo</c> (comparación ordinal) y sus fases por <c>orden</c>.
/// </summary>
public interface ILectorDeTorneosDeLaLiga
{
    Task<IReadOnlyList<TorneoDelCatalogo>> ListarAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Lectura pública del catálogo de una liga (Spec 011 R-01): una sola foto de la base (R-20, C-12) y ninguna escritura (R-19).
/// El nombre y el slug de la liga los pone el endpoint desde la resolución del slug: aquí no se lee <c>ligas</c>.
/// </summary>
public sealed class ConsultaDeTorneosDeLaLiga(
    IUnidadDeTrabajo unidadDeTrabajo, ILectorDeTorneosDeLaLiga lector, ITenantContext tenantContext, ILogger<ConsultaDeTorneosDeLaLiga> logger)
{
    public async Task<IReadOnlyList<TorneoDelCatalogo>> EjecutarAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<TorneoDelCatalogo> torneos;
        await using (var transaccion = await unidadDeTrabajo.IniciarLecturaConsistenteAsync(cancellationToken))
        {
            torneos = await lector.ListarAsync(cancellationToken);
            await transaccion.ConfirmarAsync(cancellationToken);
        }

        // Nunca nombres de torneos, de equipos ni de jugadores (R-26).
        logger.LogInformation(
            "Lectura pública: liga {Liga}, ruta {Ruta}, torneos {Torneos}, fases {Fases}",
            tenantContext.Current.Value, "torneos", torneos.Count, torneos.Sum(t => t.Fases.Count));
        return torneos;
    }
}
