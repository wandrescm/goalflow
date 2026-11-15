using GoalFlow.Application.Persistencia;
using GoalFlow.Application.Torneos.Contracts;
using GoalFlow.Domain.Torneos;
using GoalFlow.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace GoalFlow.Infrastructure.Torneos;

/// <summary>
/// Lee el torneo con todo lo que necesitan las estadísticas (Spec 009 R-30). Corre dentro de la transacción de quien llama,
/// con los filtros Tenant siempre activos, sin <c>IgnoreQueryFilters</c> y sin SQL crudo. Un número fijo de comandos
/// (R-29): uno para el torneo y siete para sus colecciones, sin importar cuántos partidos, equipos o jugadores haya.
/// El adaptador traduce las entidades a los records del contrato: ningún identificador interno sale de aquí.
/// </summary>
public sealed class LectorDeTorneoParaEstadisticas(GoalFlowDbContext contexto) : ILectorDeTorneoParaEstadisticas
{
    public async Task<DatosDeTorneoParaEstadisticas?> LeerAsync(string codigoDeTorneo, CancellationToken cancellationToken)
    {
        if (contexto.Database.CurrentTransaction is null) throw new TransaccionRequeridaException();

        var torneo = await contexto.Torneos.AsNoTracking().SingleOrDefaultAsync(t => t.Codigo == codigoDeTorneo, cancellationToken);
        if (torneo is null) return null;
        var id = torneo.Id;

        var fases = await contexto.Fases.AsNoTracking().Where(f => f.TorneoId == id)
            .Select(f => new { f.Id, f.Orden, f.Vueltas }).ToListAsync(cancellationToken);

        var equipos = await (
            from p in contexto.EquiposTorneo.AsNoTracking()
            join e in contexto.Equipos on p.EquipoId equals e.Id
            where p.TorneoId == id
            select new { p.Id, e.Codigo, e.Nombre, p.Estado }).ToListAsync(cancellationToken);

        var jugadores = await (
            from i in contexto.InscripcionesJugador.AsNoTracking()
            join j in contexto.Jugadores on i.JugadorId equals j.Id
            where i.TorneoId == id
            select new { i.Id, i.EquipoTorneoId, j.Codigo, j.Nombre }).ToListAsync(cancellationToken);

        var partidos = await (
            from p in contexto.Partidos.AsNoTracking()
            join pl in contexto.Planillas on new { P = p.Id, V = p.Version } equals new { P = pl.PartidoId, V = pl.Version } into planillas
            from pl in planillas.DefaultIfEmpty()
            where p.TorneoId == id
            select new
            {
                p.Id, p.FaseId, p.LocalId, p.VisitanteId, p.Estado,
                PlanillaId = pl == null ? (Guid?)null : pl.Id,
                Version = pl == null ? 0 : pl.Version,
                MarcadorLocal = pl == null ? null : pl.MarcadorLocal,
                MarcadorVisitante = pl == null ? null : pl.MarcadorVisitante,
                OficialLocal = pl == null ? null : pl.OficialLocal,
                OficialVisitante = pl == null ? null : pl.OficialVisitante,
                Motivo = pl == null ? null : pl.Motivo,
                TarjetasInformadas = pl == null ? null : pl.TarjetasInformadas,
            }).ToListAsync(cancellationToken);

        // Solo las filas de la planilla vigente de cada partido: la unión por (partido, versión) es la misma de arriba.
        var goles = await (
            from g in contexto.Goles.AsNoTracking()
            join pl in contexto.Planillas on g.PlanillaId equals pl.Id
            join p in contexto.Partidos on pl.PartidoId equals p.Id
            where p.TorneoId == id && pl.Version == p.Version
            select new { g.PlanillaId, g.Tipo, g.EquipoTorneoId, g.InscripcionId, g.Cantidad }).ToListAsync(cancellationToken);

        var tarjetas = await (
            from t in contexto.Tarjetas.AsNoTracking()
            join pl in contexto.Planillas on t.PlanillaId equals pl.Id
            join p in contexto.Partidos on pl.PartidoId equals p.Id
            where p.TorneoId == id && pl.Version == p.Version
            select new { t.PlanillaId, t.InscripcionId, t.Color, t.Cantidad }).ToListAsync(cancellationToken);

        // R-03: el reglamento vigente es la versión más alta.
        var reglamento = await contexto.ReglamentosTorneo.AsNoTracking()
            .Where(r => r.TorneoId == id).OrderByDescending(r => r.Version).FirstOrDefaultAsync(cancellationToken);

        var codigoDeEquipo = equipos.ToDictionary(e => e.Id, e => e.Codigo);
        var codigoDeJugador = jugadores.ToDictionary(j => j.Id, j => j.Codigo);
        var ordenDeFase = fases.ToDictionary(f => f.Id, f => f.Orden);
        var golesPorPlanilla = goles.ToLookup(g => g.PlanillaId);
        var tarjetasPorPlanilla = tarjetas.ToLookup(t => t.PlanillaId);

        return new DatosDeTorneoParaEstadisticas(
            torneo.Codigo,
            torneo.Nombre,
            [.. fases.Select(f => new FaseParaEstadisticas(f.Orden, f.Vueltas))],
            [.. equipos.Select(e => new EquipoParaEstadisticas(e.Codigo, e.Nombre, e.Estado == EstadoDeParticipacion.Inscrito))],
            [.. jugadores.Select(j => new JugadorParaEstadisticas(j.Codigo, j.Nombre, codigoDeEquipo[j.EquipoTorneoId]))],
            [.. partidos.Select(p => new PartidoParaEstadisticas(
                ordenDeFase[p.FaseId],
                codigoDeEquipo[p.LocalId],
                codigoDeEquipo[p.VisitanteId],
                Estado(p.Estado),
                p.PlanillaId is not { } planillaId
                    ? null
                    : new PlanillaVigenteParaEstadisticas(
                        p.Version, p.MarcadorLocal, p.MarcadorVisitante, p.OficialLocal, p.OficialVisitante,
                        p.Motivo?.ToString(), p.TarjetasInformadas,
                        [.. golesPorPlanilla[planillaId].Select(g => new GolParaEstadisticas(
                            Enum.Parse<TipoDeGolParaEstadisticas>(g.Tipo.ToString()),
                            codigoDeEquipo[g.EquipoTorneoId],
                            g.InscripcionId is { } inscripcion ? codigoDeJugador[inscripcion] : null,
                            g.Cantidad))],
                        [.. tarjetasPorPlanilla[planillaId].Select(t => new TarjetaParaEstadisticas(
                            codigoDeJugador[t.InscripcionId], Enum.Parse<ColorDeTarjetaParaEstadisticas>(t.Color.ToString()), t.Cantidad))])))],
            reglamento is null ? null : Reglamento(reglamento));
    }

    private static EstadoDePartidoParaEstadisticas Estado(EstadoDePartido estado) =>
        Enum.Parse<EstadoDePartidoParaEstadisticas>(estado.ToString());

    private static ReglamentoParaEstadisticas Reglamento(ReglamentoTorneo r)
    {
        var c = r.Contenido;
        AdjudicacionParaEstadisticas? Motivo(AdjudicacionDeMotivo? m) => m is null
            ? null
            : new AdjudicacionParaEstadisticas(
                m.PuntosGanador, m.PuntosPerdedor, m.PuntosEmpate, m.DeduccionPerdedor, m.DeduccionEmpate,
                m.CuentaComoJugado, m.GolesOficialesEnTabla, m.GolesOficialesEnValla, m.GolesDeportivosEnGoleadores, m.TarjetasEnFairPlay);

        Dictionary<string, AdjudicacionParaEstadisticas>? adjudicacion = null;
        if (c.Adjudicacion is { } a)
        {
            adjudicacion = new Dictionary<string, AdjudicacionParaEstadisticas>(StringComparer.Ordinal);
            foreach (var (nombre, motivo) in new[]
            {
                (nameof(MotivoDeResultado.WalkOver), a.WalkOver), (nameof(MotivoDeResultado.Sancion), a.Sancion), (nameof(MotivoDeResultado.Otro), a.Otro),
            })
                if (Motivo(motivo) is { } valor) adjudicacion[nombre] = valor;
        }

        return new ReglamentoParaEstadisticas(
            r.Version,
            r.Estado.ToString(),
            c.Puntos is { } p ? new PuntosParaEstadisticas(p.Victoria, p.Empate, p.Derrota) : null,
            c.Desempate is { } d
                ? new DesempateParaEstadisticas([.. d.Criterios.Select(x => Enum.Parse<CriterioParaEstadisticas>(x.ToString()))], d.DirectoRecursivo)
                : null,
            c.FairPlay is { } f ? new FairPlayParaEstadisticas(f.Amarilla, f.Azul, f.Roja) : null,
            adjudicacion);
    }
}
