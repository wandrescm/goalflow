using GoalFlow.Domain.Tenancy;

namespace GoalFlow.Domain.IA;

/// <summary>
/// Un resumen de partido guardado (Spec 007 R-01). Inmutable: la base no concede UPDATE ni DELETE. Una fila por
/// (tenant, partido, huella de la entrada). <see cref="CreadoEn"/> lo pone el reloj de la base.
/// </summary>
public class ResumenPartido(
    Guid id, TenantId tenantId, Guid torneoId, Guid partidoId, int versionOrigen, string huella,
    OrigenDeResumen origen, EstadoDeResumen estado, MotivoDeBorrador? motivoBorrador, string texto,
    string? modelo, string? versionPrompt, Guid? llamadaId)
{
    public Guid Id { get; private set; } = id;
    public TenantId TenantId { get; private set; } = tenantId;
    public Guid TorneoId { get; private set; } = torneoId;
    public Guid PartidoId { get; private set; } = partidoId;
    public int VersionOrigen { get; private set; } = versionOrigen;
    public string Huella { get; private set; } = huella;
    public OrigenDeResumen Origen { get; private set; } = origen;
    public EstadoDeResumen Estado { get; private set; } = estado;
    public MotivoDeBorrador? MotivoBorrador { get; private set; } = motivoBorrador;
    public string Texto { get; private set; } = texto;
    public string? Modelo { get; private set; } = modelo;
    public string? VersionPrompt { get; private set; } = versionPrompt;
    public Guid? LlamadaId { get; private set; } = llamadaId;
    public DateTime CreadoEn { get; private set; }
}
