using GoalFlow.Domain.Tenancy;

namespace GoalFlow.Domain.Torneos;

public enum TipoDeGol
{
    Jugador,
    Autogol,
    SinAutor,
}

public enum ColorDeTarjeta
{
    Amarilla,
    Azul,
    Roja,
}

/// <summary>Motivo deportivo de una suspensión o de una adjudicación (Spec 006 R-11). Códigos, nunca texto libre (C-14).</summary>
public enum MotivoDeResultado
{
    Clima,
    Luz,
    Disturbios,
    WalkOver,
    Sancion,
    Otro,
}

/// <summary>Motivo de rectificación de un cambio excepcional (Spec 006 R-14). Aparte del motivo deportivo (C-21).</summary>
public enum MotivoDeRectificacion
{
    ErrorDeRegistro,
    PartidoEquivocado,
    DecisionReglamentaria,
    Otro,
}

/// <summary>
/// Una versión de la planilla de un partido (Spec 006 R-02). Inmutable: una corrección es una versión nueva (R-21), y la base
/// no concede UPDATE ni DELETE sobre la tabla (R-06). <see cref="GuardadoEn"/> lo pone el reloj de la base.
/// </summary>
public class Planilla(
    Guid id, TenantId tenantId, Guid torneoId, Guid partidoId, int version, EstadoDePartido estado,
    int? marcadorLocal, int? marcadorVisitante, int? oficialLocal, int? oficialVisitante,
    MotivoDeResultado? motivo, MotivoDeRectificacion? motivoRectificacion, bool? tarjetasInformadas, Guid guardadoPor)
{
    public Guid Id { get; private set; } = id;
    public TenantId TenantId { get; private set; } = tenantId;
    public Guid TorneoId { get; private set; } = torneoId;
    public Guid PartidoId { get; private set; } = partidoId;
    public int Version { get; private set; } = version;
    public EstadoDePartido Estado { get; private set; } = estado;
    public int? MarcadorLocal { get; private set; } = marcadorLocal;
    public int? MarcadorVisitante { get; private set; } = marcadorVisitante;
    public int? OficialLocal { get; private set; } = oficialLocal;
    public int? OficialVisitante { get; private set; } = oficialVisitante;
    public MotivoDeResultado? Motivo { get; private set; } = motivo;
    public MotivoDeRectificacion? MotivoRectificacion { get; private set; } = motivoRectificacion;
    public bool? TarjetasInformadas { get; private set; } = tarjetasInformadas;

    /// <summary>El <c>sub</c> del Admin autenticado: identificador interno, nunca se devuelve (Spec 006 §4).</summary>
    public Guid GuardadoPor { get; private set; } = guardadoPor;

    public DateTimeOffset GuardadoEn { get; private set; }
}

/// <summary>Fila de goles de una planilla: (tipo, participación acreditada, inscripción, cantidad). Spec 006 R-03.</summary>
public class Gol(
    Guid id, TenantId tenantId, Guid torneoId, Guid planillaId, TipoDeGol tipo, Guid equipoTorneoId, Guid? inscripcionId, int cantidad)
{
    public Guid Id { get; private set; } = id;
    public TenantId TenantId { get; private set; } = tenantId;
    public Guid TorneoId { get; private set; } = torneoId;
    public Guid PlanillaId { get; private set; } = planillaId;
    public TipoDeGol Tipo { get; private set; } = tipo;

    /// <summary>La participación a la que se acredita el gol. En un autogol es la del rival del autor.</summary>
    public Guid EquipoTorneoId { get; private set; } = equipoTorneoId;

    /// <summary>Ausente si y solo si el tipo es <see cref="TipoDeGol.SinAutor"/>.</summary>
    public Guid? InscripcionId { get; private set; } = inscripcionId;

    public int Cantidad { get; private set; } = cantidad;
}

/// <summary>Fila de tarjetas de una planilla: (inscripción, color, cantidad). Spec 006 R-04.</summary>
public class Tarjeta(
    Guid id, TenantId tenantId, Guid torneoId, Guid planillaId, Guid inscripcionId, ColorDeTarjeta color, int cantidad)
{
    public Guid Id { get; private set; } = id;
    public TenantId TenantId { get; private set; } = tenantId;
    public Guid TorneoId { get; private set; } = torneoId;
    public Guid PlanillaId { get; private set; } = planillaId;
    public Guid InscripcionId { get; private set; } = inscripcionId;
    public ColorDeTarjeta Color { get; private set; } = color;
    public int Cantidad { get; private set; } = cantidad;
}
