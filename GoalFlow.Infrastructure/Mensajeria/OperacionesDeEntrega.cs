using GoalFlow.Application.Persistencia;
using GoalFlow.Domain.Tenancy;
using GoalFlow.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GoalFlow.Infrastructure.Mensajeria;

/// <summary>Una fila disponible, tal como se leyó. El reclamo solo prospera si la fila sigue igual (mismos intentos).</summary>
public sealed record EntregaCandidata(
    Guid Id, TenantId Tenant, string TipoDeEvento, Guid AgregadoId, int Version, string Consumidor, int Intentos);

/// <summary>Una entrega reclamada por este trabajador: lleva el token del lease con el que debe terminar.</summary>
public sealed record EntregaReclamada(
    Guid Id, TenantId Tenant, string TipoDeEvento, Guid AgregadoId, int Version, string Consumidor, int Intentos, Guid Token);

/// <summary>
/// Reclamación, finalización y fallo con compare-and-swap de <c>ExecuteUpdateAsync</c> (ADR-02 §2.6 y §2.7).
/// Todo timestamp sale de <c>DateTime.UtcNow</c> dentro de la expresión (Npgsql lo traduce a <c>now()</c>);
/// solo viajan duraciones como parámetros (R-26). Cada operación exige una transacción activa (R-27):
/// sin ella, RLS dejaría cero filas y se vería igual que perder la carrera.
/// </summary>
public sealed class OperacionesDeEntrega(
    GoalFlowDbContext contexto,
    IOptions<OpcionesDeMensajeria> opciones,
    IEnumerable<RegistroDeConsumidor> registros,
    ILogger<OperacionesDeEntrega> logger)
{
    private readonly OpcionesDeMensajeria _o = opciones.Value;

    /// <summary>Hasta <c>LotePorTenant</c> entregas disponibles de los consumidores registrados en este host (R-22, R-23, R-45).</summary>
    public async Task<IReadOnlyList<EntregaCandidata>> ListarCandidatasAsync(CancellationToken cancellationToken)
    {
        ExigirTransaccion();
        var nombres = registros.Select(r => r.Nombre).Distinct().ToList();

        return await contexto.EntregasEventos.AsNoTracking()
            .Where(e => nombres.Contains(e.Consumidor)
                && ((e.Estado == EstadoDeEntrega.Pendiente && e.ProximoIntentoEn <= DateTime.UtcNow)
                    || (e.Estado == EstadoDeEntrega.EnProceso && e.LeaseVenceEn < DateTime.UtcNow)))
            .OrderBy(e => e.CreadoEn).ThenBy(e => e.Id)
            .Take(_o.LotePorTenant)
            .Select(e => new EntregaCandidata(
                e.Id, e.TenantId, e.TipoDeEvento, e.AgregadoId, e.VersionDelAgregado, e.Consumidor, e.Intentos))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Reclama la entrega (R-24): <c>EnProceso</c>, token nuevo, lease = ahora + duración e intentos + 1, en una sola
    /// sentencia. Devuelve null si no afectó exactamente una fila: otro trabajador ganó, o ya agotó los intentos.
    /// </summary>
    public async Task<EntregaReclamada?> ReclamarAsync(EntregaCandidata candidata, CancellationToken cancellationToken)
    {
        ExigirTransaccion();
        var token = Guid.NewGuid();
        var id = candidata.Id;
        var intentosLeidos = candidata.Intentos;
        var maximo = _o.MaximoDeIntentos;
        var segundosDelLease = _o.DuracionDelLease.TotalSeconds;
        var nombres = registros.Select(r => r.Nombre).Distinct().ToList();

        var afectadas = await contexto.EntregasEventos
            .Where(e => e.Id == id
                && nombres.Contains(e.Consumidor)
                && e.Intentos == intentosLeidos
                && e.Intentos < maximo
                && ((e.Estado == EstadoDeEntrega.Pendiente && e.ProximoIntentoEn <= DateTime.UtcNow)
                    || (e.Estado == EstadoDeEntrega.EnProceso && e.LeaseVenceEn < DateTime.UtcNow)))
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.Estado, EstadoDeEntrega.EnProceso)
                .SetProperty(e => e.TokenDelLease, token)
                .SetProperty(e => e.LeaseVenceEn, _ => DateTime.UtcNow.AddSeconds(segundosDelLease))
                .SetProperty(e => e.Intentos, e => e.Intentos + 1), cancellationToken);

        return afectadas == 1
            ? new EntregaReclamada(
                candidata.Id, candidata.Tenant, candidata.TipoDeEvento, candidata.AgregadoId, candidata.Version,
                candidata.Consumidor, candidata.Intentos + 1, token)
            : null;
    }

    /// <summary>
    /// Una entrega disponible que ya agotó sus intentos nunca llega a un consumidor: pasa a <c>Fallida</c> (R-28, R-36).
    /// </summary>
    public async Task<bool> AgotarAsync(EntregaCandidata candidata, CancellationToken cancellationToken)
    {
        ExigirTransaccion();
        var id = candidata.Id;
        var maximo = _o.MaximoDeIntentos;
        var nombres = registros.Select(r => r.Nombre).Distinct().ToList();

        var afectadas = await contexto.EntregasEventos
            .Where(e => e.Id == id
                && nombres.Contains(e.Consumidor)
                && e.Intentos >= maximo
                && ((e.Estado == EstadoDeEntrega.Pendiente && e.ProximoIntentoEn <= DateTime.UtcNow)
                    || (e.Estado == EstadoDeEntrega.EnProceso && e.LeaseVenceEn < DateTime.UtcNow)))
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.Estado, EstadoDeEntrega.Fallida)
                .SetProperty(e => e.UltimoError, e => e.UltimoError ?? "IntentosAgotados"), cancellationToken);

        if (afectadas == 1)
            logger.LogWarning(
                "Entrega {EntregaId} fallida por intentos agotados. Tenant {Tenant}, evento {Evento}, consumidor {Consumidor}, intentos {Intentos}.",
                candidata.Id, candidata.Tenant.Value, candidata.TipoDeEvento, candidata.Consumidor, candidata.Intentos);
        return afectadas == 1;
    }

    /// <summary>Completa condicionada a id, <c>EnProceso</c> y el token del lease (R-31). False: el lease se perdió (R-34).</summary>
    public async Task<bool> CompletarAsync(EntregaReclamada entrega, CancellationToken cancellationToken)
    {
        ExigirTransaccion();
        var (id, token) = (entrega.Id, entrega.Token);

        var afectadas = await contexto.EntregasEventos
            .Where(e => e.Id == id && e.Estado == EstadoDeEntrega.EnProceso && e.TokenDelLease == token)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.Estado, EstadoDeEntrega.Completada)
                .SetProperty(e => e.CompletadoEn, _ => DateTime.UtcNow), cancellationToken);

        return RegistrarLeasePerdido(entrega, afectadas);
    }

    /// <summary>
    /// Fallo reintentable: <c>Pendiente</c> con próximo intento = <c>now()</c> + backoff (R-32). Solo el backoff,
    /// una duración, lo calcula la aplicación. <paramref name="tipoDeError"/> es el tipo de la excepción, nunca su mensaje.
    /// </summary>
    public async Task<bool> ReintentarAsync(EntregaReclamada entrega, string tipoDeError, CancellationToken cancellationToken)
    {
        ExigirTransaccion();
        var (id, token) = (entrega.Id, entrega.Token);
        var segundos = CalcularBackoff(entrega.Intentos).TotalSeconds;

        var afectadas = await contexto.EntregasEventos
            .Where(e => e.Id == id && e.Estado == EstadoDeEntrega.EnProceso && e.TokenDelLease == token)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.Estado, EstadoDeEntrega.Pendiente)
                .SetProperty(e => e.ProximoIntentoEn, _ => DateTime.UtcNow.AddSeconds(segundos))
                .SetProperty(e => e.LeaseVenceEn, _ => (DateTime?)null)
                .SetProperty(e => e.UltimoError, tipoDeError), cancellationToken);

        return RegistrarLeasePerdido(entrega, afectadas);
    }

    /// <summary>Fallo permanente: <c>Fallida</c> con el código de razón del consumidor (R-33, R-36).</summary>
    public async Task<bool> FallarAsync(EntregaReclamada entrega, string codigoDeRazon, CancellationToken cancellationToken)
    {
        ExigirTransaccion();
        var (id, token) = (entrega.Id, entrega.Token);

        var afectadas = await contexto.EntregasEventos
            .Where(e => e.Id == id && e.Estado == EstadoDeEntrega.EnProceso && e.TokenDelLease == token)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.Estado, EstadoDeEntrega.Fallida)
                .SetProperty(e => e.LeaseVenceEn, _ => (DateTime?)null)
                .SetProperty(e => e.UltimoError, codigoDeRazon), cancellationToken);

        if (afectadas == 1)
            logger.LogWarning(
                "Entrega {EntregaId} fallida de forma permanente. Tenant {Tenant}, evento {Evento}, consumidor {Consumidor}, intentos {Intentos}.",
                entrega.Id, entrega.Tenant.Value, entrega.TipoDeEvento, entrega.Consumidor, entrega.Intentos);
        return RegistrarLeasePerdido(entrega, afectadas);
    }

    /// <summary>30 s × 2^(intento − 1), con tope (ADR-02 §2.8).</summary>
    public TimeSpan CalcularBackoff(int intento)
    {
        var potencia = Math.Pow(2, Math.Clamp(intento - 1, 0, 30));
        var segundos = Math.Min(_o.BackoffInicial.TotalSeconds * potencia, _o.BackoffMaximo.TotalSeconds);
        return TimeSpan.FromSeconds(segundos);
    }

    private void ExigirTransaccion()
    {
        if (contexto.Database.CurrentTransaction is null)
            throw new TransaccionRequeridaException();
    }

    private bool RegistrarLeasePerdido(EntregaReclamada entrega, int afectadas)
    {
        if (afectadas == 1) return true;

        logger.LogWarning(
            "Lease perdido para la entrega {EntregaId}: no se escribió el resultado y no se reintenta. Tenant {Tenant}, evento {Evento}, consumidor {Consumidor}, intento {Intento}.",
            entrega.Id, entrega.Tenant.Value, entrega.TipoDeEvento, entrega.Consumidor, entrega.Intentos);
        return false;
    }
}
