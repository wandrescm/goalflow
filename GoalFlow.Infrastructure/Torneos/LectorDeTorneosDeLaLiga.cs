using GoalFlow.Application.Persistencia;
using GoalFlow.Application.Torneos;
using GoalFlow.Domain.Torneos;
using GoalFlow.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace GoalFlow.Infrastructure.Torneos;

/// <summary>
/// Los torneos y fases del tenant actual (Spec 011 R-01, R-28). Corre dentro de la transacción de quien llama, con el filtro
/// <c>Tenant</c> siempre activo, en un solo comando (R-21). El orden lo fija el código y no la base: comparación ordinal (R-22).
/// </summary>
public sealed class LectorDeTorneosDeLaLiga(GoalFlowDbContext contexto) : ILectorDeTorneosDeLaLiga
{
    public async Task<IReadOnlyList<TorneoDelCatalogo>> ListarAsync(CancellationToken cancellationToken)
    {
        if (contexto.Database.CurrentTransaction is null) throw new TransaccionRequeridaException();

        // Un solo comando para todos los torneos y sus fases, sea cual sea su número.
        var filas = await (
            from t in contexto.Torneos.AsNoTracking()
            join f in contexto.Fases on t.Id equals f.TorneoId into fases
            from f in fases.DefaultIfEmpty()
            select new { t.Id, t.Codigo, t.Nombre, Orden = (int?)f!.Orden, Tipo = (TipoDeFase?)f.Tipo, Vueltas = (int?)f.Vueltas })
            .ToListAsync(cancellationToken);

        return
        [
            .. filas
                .GroupBy(f => (f.Id, f.Codigo, f.Nombre))
                .Select(g => new TorneoDelCatalogo(
                    g.Key.Codigo, g.Key.Nombre,
                    [.. g.Where(f => f.Orden is not null).OrderBy(f => f.Orden!.Value)
                        .Select(f => new FaseDelCatalogo(f.Orden!.Value, f.Tipo!.Value.ToString(), f.Vueltas!.Value))]))
                .OrderBy(t => t.Codigo, StringComparer.Ordinal),
        ];
    }
}
