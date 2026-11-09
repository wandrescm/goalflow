using GoalFlow.Application.Torneos;
using GoalFlow.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GoalFlow.Infrastructure.Torneos;

/// <summary>
/// Adaptador del puerto de la importación (Spec 005 §2.3). Corre dentro de la transacción de la unidad de trabajo,
/// con los filtros Tenant del DbContext siempre activos.
/// </summary>
public class AlmacenDeImportacion(GoalFlowDbContext contexto) : IAlmacenDeImportacion
{
    public async Task<EstadoGuardado> CargarAsync(
        string codigoDeTorneo,
        IReadOnlyCollection<string> codigosDeEquipo,
        IReadOnlyCollection<string> codigosDeJugador,
        CancellationToken cancellationToken)
    {
        var torneo = await contexto.Torneos.SingleOrDefaultAsync(t => t.Codigo == codigoDeTorneo, cancellationToken);

        var fases = new List<Domain.Torneos.Fase>();
        var participaciones = new List<Domain.Torneos.EquipoTorneo>();
        var inscripciones = new List<Domain.Torneos.InscripcionJugador>();
        var partidos = new List<Domain.Torneos.Partido>();
        if (torneo is not null)
        {
            var id = torneo.Id;
            fases = await contexto.Fases.Where(f => f.TorneoId == id).ToListAsync(cancellationToken);
            participaciones = await contexto.EquiposTorneo.Where(p => p.TorneoId == id).ToListAsync(cancellationToken);
            inscripciones = await contexto.InscripcionesJugador.Where(i => i.TorneoId == id).ToListAsync(cancellationToken);
            partidos = await contexto.Partidos.Where(p => p.TorneoId == id).ToListAsync(cancellationToken);
        }

        var equiposDelTorneo = participaciones.Select(p => p.EquipoId).ToList();
        var jugadoresDelTorneo = inscripciones.Select(i => i.JugadorId).ToList();
        var equipos = await contexto.Equipos
            .Where(e => codigosDeEquipo.Contains(e.Codigo) || equiposDelTorneo.Contains(e.Id))
            .ToListAsync(cancellationToken);
        var jugadores = await contexto.Jugadores
            .Where(j => codigosDeJugador.Contains(j.Codigo) || jugadoresDelTorneo.Contains(j.Id))
            .ToListAsync(cancellationToken);

        return new EstadoGuardado(torneo, fases, equipos, jugadores, participaciones, inscripciones, partidos);
    }

    public void Agregar<T>(T entidad) where T : class => contexto.Add(entidad);

    public async Task GuardarAsync(CancellationToken cancellationToken)
    {
        try
        {
            await contexto.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Otra transacción cambió un partido después de leerlo, por ejemplo una planilla (Spec 006 R-20, R-30). Es el
            // mismo 409 de la importación concurrente, sin nombres de restricción.
            throw new ConflictoConcurrenteException();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Sin el nombre de la restricción ni el mensaje de la base (R-23, R-24).
            throw new ConflictoConcurrenteException();
        }
    }
}
