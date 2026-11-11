using GoalFlow.Application.Persistencia;
using GoalFlow.Application.Torneos.Contracts;
using GoalFlow.Domain.Torneos;
using GoalFlow.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace GoalFlow.Infrastructure.Torneos;

/// <summary>
/// Lee la planilla vigente de un partido resuelta a nombres para IA (Spec 007 R-33). Corre dentro de la transacción de quien llama,
/// con los filtros Tenant siempre activos. Lee ese partido, su planilla vigente con sus filas y los nombres que intervienen, y nada más.
/// </summary>
public sealed class LectorDePartidoParaResumen(GoalFlowDbContext contexto) : ILectorDePartidoParaResumen
{
    public async Task<DatosDePartidoParaResumen?> LeerAsync(Guid partidoId, CancellationToken cancellationToken)
    {
        if (contexto.Database.CurrentTransaction is null) throw new TransaccionRequeridaException();

        var partido = await (
            from p in contexto.Partidos.AsNoTracking()
            join f in contexto.Fases on p.FaseId equals f.Id
            join t in contexto.Torneos on f.TorneoId equals t.Id
            join l in contexto.EquiposTorneo on p.LocalId equals l.Id
            join v in contexto.EquiposTorneo on p.VisitanteId equals v.Id
            join el in contexto.Equipos on l.EquipoId equals el.Id
            join ev in contexto.Equipos on v.EquipoId equals ev.Id
            where p.Id == partidoId
            select new
            {
                p.TorneoId, p.Version, p.Estado, p.Jornada, p.LocalId, p.VisitanteId, p.ProgramadoEn,
                Torneo = t.Nombre, Fase = f.Orden, Local = el.Nombre, Visitante = ev.Nombre,
            }).SingleOrDefaultAsync(cancellationToken);
        if (partido is null) return null;

        Planilla? planilla = null;
        List<Gol> goles = [];
        List<Tarjeta> tarjetas = [];
        if (partido.Version > 0)
        {
            var version = partido.Version;
            planilla = await contexto.Planillas.AsNoTracking()
                .SingleAsync(p => p.PartidoId == partidoId && p.Version == version, cancellationToken);
            var planillaId = planilla.Id;
            goles = await contexto.Goles.AsNoTracking().Where(g => g.PlanillaId == planillaId).ToListAsync(cancellationToken);
            tarjetas = await contexto.Tarjetas.AsNoTracking().Where(t => t.PlanillaId == planillaId).ToListAsync(cancellationToken);
        }

        // Los nombres de los jugadores que intervienen y el lado de su equipo en este partido.
        var inscripciones = goles.Where(g => g.InscripcionId is not null).Select(g => g.InscripcionId!.Value)
            .Concat(tarjetas.Select(t => t.InscripcionId)).Distinct().ToList();
        var jugadores = await (
            from i in contexto.InscripcionesJugador.AsNoTracking()
            join j in contexto.Jugadores on i.JugadorId equals j.Id
            where inscripciones.Contains(i.Id)
            select new { i.Id, i.EquipoTorneoId, j.Nombre, j.Codigo }).ToDictionaryAsync(j => j.Id, cancellationToken);

        LadoDelEquipo Lado(Guid equipoTorneoId) => equipoTorneoId == partido.LocalId ? LadoDelEquipo.Local : LadoDelEquipo.Visitante;

        var golesDeJugador = goles.Where(g => g.Tipo == TipoDeGol.Jugador).Select(g =>
        {
            var j = jugadores[g.InscripcionId!.Value];
            return new GolDelPartido(Lado(g.EquipoTorneoId), j.Nombre, j.Codigo, g.Cantidad);
        }).ToList();

        // Un autogol se acredita al equipo del gol; su autor juega en el otro: su lado sale de su inscripción.
        var autogoles = goles.Where(g => g.Tipo == TipoDeGol.Autogol).Select(g =>
        {
            var j = jugadores[g.InscripcionId!.Value];
            return new AutogolDelPartido(j.Nombre, j.Codigo, Lado(j.EquipoTorneoId), g.Cantidad);
        }).ToList();

        int SinAutor(LadoDelEquipo lado) => goles
            .Where(g => g.Tipo == TipoDeGol.SinAutor && Lado(g.EquipoTorneoId) == lado).Sum(g => g.Cantidad);

        var tarjetasDelPartido = tarjetas.Select(t =>
        {
            var j = jugadores[t.InscripcionId];
            return new TarjetaDelPartido(Lado(j.EquipoTorneoId), j.Nombre, j.Codigo, t.Color.ToString(), t.Cantidad);
        }).ToList();

        static MarcadorDelPartido? Marcador(int? local, int? visitante) =>
            local is { } l && visitante is { } v ? new MarcadorDelPartido(l, v) : null;

        return new DatosDePartidoParaResumen(
            TorneoId: partido.TorneoId,
            Version: partido.Version,
            Estado: partido.Estado.ToString(),
            Torneo: partido.Torneo,
            Fase: partido.Fase,
            Jornada: partido.Jornada,
            Local: partido.Local,
            Visitante: partido.Visitante,
            Marcador: Marcador(planilla?.MarcadorLocal, planilla?.MarcadorVisitante),
            Oficial: Marcador(planilla?.OficialLocal, planilla?.OficialVisitante),
            Motivo: planilla?.Motivo?.ToString(),
            Goles: golesDeJugador,
            Autogoles: autogoles,
            GolesSinAutor: new MarcadorDelPartido(SinAutor(LadoDelEquipo.Local), SinAutor(LadoDelEquipo.Visitante)),
            TarjetasInformadas: planilla?.TarjetasInformadas,
            Tarjetas: tarjetasDelPartido,
            MotivoDeRectificacion: planilla?.MotivoRectificacion?.ToString(),
            ProgramadoPara: partido.ProgramadoEn?.UtcDateTime);
    }
}
