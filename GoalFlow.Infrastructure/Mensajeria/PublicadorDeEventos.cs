using GoalFlow.Application.Mensajeria;
using GoalFlow.Application.Persistencia;
using GoalFlow.Infrastructure.Persistencia;
using Microsoft.Extensions.Logging;

namespace GoalFlow.Infrastructure.Mensajeria;

/// <summary>
/// Agrega una fila por consumidor registrado al DbContext del productor y no guarda (Spec 004 R-05).
/// El aviso al ciclo lo envía el interceptor tras el commit; aquí solo se anota el tenant.
/// </summary>
public sealed class PublicadorDeEventos(
    GoalFlowDbContext contexto,
    IEnumerable<RegistroDeConsumidor> consumidores,
    AvisosPendientes avisos,
    ILogger<PublicadorDeEventos> logger) : IPublicadorDeEventos
{
    public void Publicar<TEvento>(TEvento evento) where TEvento : IEventoDeIntegracion
    {
        if (contexto.Database.CurrentTransaction is null)
            throw new TransaccionRequeridaException();

        var nombreDelEvento = TEvento.NombreDelEvento;
        var destinos = consumidores.Where(c => c.NombreDelEvento == nombreDelEvento).ToList();
        if (destinos.Count == 0)
        {
            logger.LogInformation("Evento {Evento} publicado sin consumidores registrados; no se escribe ninguna entrega.", nombreDelEvento);
            return;
        }

        var eventoId = Guid.NewGuid();
        foreach (var destino in destinos)
            contexto.EntregasEventos.Add(EntregaEvento.Nueva(
                evento.TenantId, eventoId, nombreDelEvento, evento.AgregadoId, evento.Version, destino.Nombre));

        avisos.Anotar(evento.TenantId);
    }

    public void PublicarPara<TEvento>(TEvento evento, string consumidor) where TEvento : IEventoDeIntegracion
    {
        if (contexto.Database.CurrentTransaction is null)
            throw new TransaccionRequeridaException();

        // Solo cuenta un consumidor registrado para ESTE tipo de evento: un nombre que existe para otro evento no sirve (R-31).
        var nombreDelEvento = TEvento.NombreDelEvento;
        if (!consumidores.Any(c => c.NombreDelEvento == nombreDelEvento && c.Nombre == consumidor))
            throw new ConsumidorNoRegistradoException(consumidor);

        contexto.EntregasEventos.Add(EntregaEvento.Nueva(
            evento.TenantId, Guid.NewGuid(), nombreDelEvento, evento.AgregadoId, evento.Version, consumidor));
        avisos.Anotar(evento.TenantId);
    }
}
