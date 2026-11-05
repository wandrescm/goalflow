using System.Threading.Channels;
using GoalFlow.Domain.Tenancy;
using Microsoft.Extensions.Options;

namespace GoalFlow.Infrastructure.Mensajeria;

/// <summary>
/// Canal acotado que solo despierta al ciclo: lleva el TenantId como pista, nunca trabajo (ADR-02 §2.1).
/// Perder una pista no pierde trabajo: el temporizador lo encuentra en la base. Singleton.
/// </summary>
public sealed class CanalDeAvisos
{
    private readonly Channel<TenantId> _canal;

    public CanalDeAvisos(IOptions<OpcionesDeMensajeria> opciones)
    {
        // Wait con un solo lector (R-18). Nunca DropOldest ni DropNewest: el productor no espera, usa TryWrite.
        Configuracion = new BoundedChannelOptions(opciones.Value.CapacidadDelCanal)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
        };
        _canal = Channel.CreateBounded<TenantId>(Configuracion);
    }

    public BoundedChannelOptions Configuracion { get; }

    public ChannelReader<TenantId> Lector => _canal.Reader;

    /// <summary>Nunca espera capacidad: con el canal lleno devuelve false y la pista se pierde (R-10).</summary>
    public bool IntentarAvisar(TenantId tenant) => _canal.Writer.TryWrite(tenant);
}
