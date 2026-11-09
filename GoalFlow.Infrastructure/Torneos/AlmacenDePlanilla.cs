using GoalFlow.Application.Torneos;
using GoalFlow.Domain.Torneos;
using GoalFlow.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GoalFlow.Infrastructure.Torneos;

/// <summary>
/// Adaptador del puerto de la planilla (Spec 006 §4). Corre dentro de la transacción de la unidad de trabajo, con los filtros
/// Tenant siempre activos. Lee el partido, su planilla vigente y las inscripciones de sus dos equipos, y nada más.
/// </summary>
public class AlmacenDePlanilla(GoalFlowDbContext contexto) : IAlmacenDePlanilla
{
    public async Task<PartidoCargado?> CargarAsync(ClaveDePartido clave, CancellationToken cancellationToken)
    {
        // El partido se rastrea: su token de fila (xmin) se lee aquí y condiciona la actualización de R-19 (R-20).
        var partido = await (
            from p in contexto.Partidos
            join f in contexto.Fases on p.FaseId equals f.Id
            join t in contexto.Torneos on f.TorneoId equals t.Id
            join l in contexto.EquiposTorneo on p.LocalId equals l.Id
            join v in contexto.EquiposTorneo on p.VisitanteId equals v.Id
            join el in contexto.Equipos on l.EquipoId equals el.Id
            join ev in contexto.Equipos on v.EquipoId equals ev.Id
            where t.Codigo == clave.Torneo && f.Orden == clave.Fase && el.Codigo == clave.Local && ev.Codigo == clave.Visitante
            select p).SingleOrDefaultAsync(cancellationToken);
        if (partido is null) return null;

        Planilla? vigente = null;
        List<Gol> goles = [];
        List<Tarjeta> tarjetas = [];
        if (partido.Version > 0)
        {
            var (id, version) = (partido.Id, partido.Version);
            vigente = await contexto.Planillas.SingleAsync(p => p.PartidoId == id && p.Version == version, cancellationToken);
            var planillaId = vigente.Id;
            goles = await contexto.Goles.Where(g => g.PlanillaId == planillaId).ToListAsync(cancellationToken);
            tarjetas = await contexto.Tarjetas.Where(t => t.PlanillaId == planillaId).ToListAsync(cancellationToken);
        }

        var (torneoId, localId, visitanteId) = (partido.TorneoId, partido.LocalId, partido.VisitanteId);
        var inscritos = await (
            from i in contexto.InscripcionesJugador
            join j in contexto.Jugadores on i.JugadorId equals j.Id
            where i.TorneoId == torneoId && (i.EquipoTorneoId == localId || i.EquipoTorneoId == visitanteId)
            select new { j.Codigo, i.Id, i.EquipoTorneoId }).ToListAsync(cancellationToken);

        return new PartidoCargado(
            partido, vigente, goles, tarjetas,
            [.. inscritos.Select(i => new JugadorInscrito(
                i.Codigo, i.Id, i.EquipoTorneoId == localId ? LadoDelPartido.Local : LadoDelPartido.Visitante))]);
    }

    public void AgregarVersion(Planilla planilla, IReadOnlyList<Gol> goles, IReadOnlyList<Tarjeta> tarjetas)
    {
        contexto.Add(planilla);
        contexto.AddRange(goles);
        contexto.AddRange(tarjetas);
    }

    public async Task GuardarAsync(CancellationToken cancellationToken)
    {
        try
        {
            await contexto.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // El token de fila de partidos cambió después de la lectura: otra planilla o una importación (R-20).
            throw new ConflictoDeVersionException(null);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: ConfiguracionDeTorneos.IndiceDeVersionDePlanilla,
        })
        {
            // Segunda guarda: la misma versión de la planilla ya existe (R-20). Sin el nombre de la restricción.
            throw new ConflictoDeVersionException(null);
        }
    }
}
