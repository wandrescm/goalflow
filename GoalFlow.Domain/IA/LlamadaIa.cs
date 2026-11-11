using GoalFlow.Domain.Tenancy;

namespace GoalFlow.Domain.IA;

/// <summary>
/// Una llamada pagada al proveedor (Spec 007 R-02): nace <c>Reservada</c> y pasa a <c>Conciliada</c> con el uso reportado.
/// La conciliación la hace un <c>ExecuteUpdateAsync</c> condicionado, no esta clase. Montos en micro-dólares.
/// </summary>
public class LlamadaIa(
    Guid id, TenantId tenantId, Guid torneoId, Guid partidoId, int version, string huella,
    int numero, int intentoEntrega, string mes, long reserva)
{
    public Guid Id { get; private set; } = id;
    public TenantId TenantId { get; private set; } = tenantId;
    public Guid TorneoId { get; private set; } = torneoId;
    public Guid PartidoId { get; private set; } = partidoId;
    public int Version { get; private set; } = version;
    public string Huella { get; private set; } = huella;
    public int Numero { get; private set; } = numero;
    public int IntentoEntrega { get; private set; } = intentoEntrega;

    /// <summary>El mes UTC <c>YYYY-MM</c> de la reserva: la conciliación actualiza el contador de este mes.</summary>
    public string Mes { get; private set; } = mes;

    public long Reserva { get; private set; } = reserva;
    public EstadoDeLlamada Estado { get; private set; } = EstadoDeLlamada.Reservada;
    public int? TokensEntrada { get; private set; }
    public int? TokensSalida { get; private set; }
    public long? Costo { get; private set; }
    public string? Resultado { get; private set; }
    public DateTime? ConciliadaEn { get; private set; }
    public DateTime CreadoEn { get; private set; }
}
