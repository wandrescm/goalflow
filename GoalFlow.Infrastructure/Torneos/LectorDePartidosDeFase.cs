using GoalFlow.Application.Persistencia;
using GoalFlow.Application.Torneos;
using GoalFlow.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace GoalFlow.Infrastructure.Torneos;

/// <summary>
/// Los partidos de una fase del tenant actual (Spec 011 R-03 a R-07, R-28). Corre dentro de la transacción de quien llama, con el filtro
/// <c>Tenant</c> siempre activo, en dos comandos que no dependen del número de partidos (R-21): la comprobación del torneo y la fase, y
/// los partidos con la planilla cuya versión es <c>partidos.version</c>. El orden lo fija el código, con comparación ordinal (R-06, R-22).
/// </summary>
public sealed class LectorDePartidosDeFase(GoalFlowDbContext contexto) : ILectorDePartidosDeFase
{
    public async Task<PartidosDeLaFase?> ListarAsync(string codigoDeTorneo, int ordenDeFase, CancellationToken cancellationToken)
    {
        if (contexto.Database.CurrentTransaction is null) throw new TransaccionRequeridaException();

        // Los códigos se comparan exactamente, como la columna: sensible a mayúsculas.
        var fase = await (
            from t in contexto.Torneos.AsNoTracking()
            join f in contexto.Fases on t.Id equals f.TorneoId
            where t.Codigo == codigoDeTorneo && f.Orden == ordenDeFase
            select new { TorneoCodigo = t.Codigo, TorneoNombre = t.Nombre, FaseId = f.Id, FaseOrden = f.Orden })
            .SingleOrDefaultAsync(cancellationToken);
        if (fase is null) return null;

        var faseId = fase.FaseId;

        // La planilla vigente es la de la versión igual a partidos.version (Spec 006 R-07); con versión 0 no hay ninguna y la unión da null.
        var filas = await (
            from p in contexto.Partidos.AsNoTracking()
            where p.FaseId == faseId
            join l in contexto.EquiposTorneo on p.LocalId equals l.Id
            join el in contexto.Equipos on l.EquipoId equals el.Id
            join v in contexto.EquiposTorneo on p.VisitanteId equals v.Id
            join ev in contexto.Equipos on v.EquipoId equals ev.Id
            join pl in contexto.Planillas on new { p.Id, p.Version } equals new { Id = pl.PartidoId, pl.Version } into planillas
            from pl in planillas.DefaultIfEmpty()
            select new
            {
                p.Jornada, p.ProgramadoEn, p.Estado,
                LocalCodigo = el.Codigo, LocalNombre = el.Nombre, VisitanteCodigo = ev.Codigo, VisitanteNombre = ev.Nombre,
                MarcadorLocal = (int?)pl.MarcadorLocal, MarcadorVisitante = (int?)pl.MarcadorVisitante,
                OficialLocal = (int?)pl.OficialLocal, OficialVisitante = (int?)pl.OficialVisitante,
                Motivo = (Domain.Torneos.MotivoDeResultado?)pl.Motivo,
            }).ToListAsync(cancellationToken);

        static MarcadorPublico? Marcador(int? local, int? visitante) =>
            local is { } l && visitante is { } v ? new MarcadorPublico(l, v) : null;

        var partidos = filas
            .Select(f => new PartidoDeLaLista(
                f.Jornada,
                f.ProgramadoEn?.UtcDateTime,
                new EquipoDeLaLista(f.LocalCodigo, f.LocalNombre),
                new EquipoDeLaLista(f.VisitanteCodigo, f.VisitanteNombre),
                f.Estado.ToString(),
                Marcador(f.MarcadorLocal, f.MarcadorVisitante),
                Marcador(f.OficialLocal, f.OficialVisitante),
                f.Motivo?.ToString()))
            .OrderBy(p => p.Jornada)
            .ThenBy(p => p.ProgramadoPara is null)                       // sin hora al final
            .ThenBy(p => p.ProgramadoPara)
            .ThenBy(p => p.Local.Codigo, StringComparer.Ordinal)
            .ThenBy(p => p.Visitante.Codigo, StringComparer.Ordinal)
            .ToList();

        return new PartidosDeLaFase(new TorneoDeLaLista(fase.TorneoCodigo, fase.TorneoNombre), new FaseDeLaLista(fase.FaseOrden), partidos);
    }
}
