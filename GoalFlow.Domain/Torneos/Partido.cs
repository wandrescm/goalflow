using GoalFlow.Domain.Tenancy;

namespace GoalFlow.Domain.Torneos;

public class Partido(
    Guid id, TenantId tenantId, Guid torneoId, Guid faseId, int jornada, Guid localId, Guid visitanteId, DateTimeOffset? programadoEn)
{
    public Guid Id { get; private set; } = id;
    public TenantId TenantId { get; private set; } = tenantId;
    public Guid TorneoId { get; private set; } = torneoId;
    public Guid FaseId { get; private set; } = faseId;
    public int Jornada { get; private set; } = Reglas.ExigirJornada(jornada);
    public Guid LocalId { get; private set; } = localId;
    public Guid VisitanteId { get; private set; } = localId == visitanteId
        ? throw new ReglaDeDominioException(RazonesDeDominio.LocalIgualAVisitante)
        : visitanteId;
    public DateTimeOffset? ProgramadoEn { get; private set; } = Reglas.HoraEnUtc(programadoEn);
    public EstadoDePartido Estado { get; private set; } = EstadoDePartido.Programado;

    /// <summary>Número de la planilla vigente; 0 si nunca se guardó una (Spec 006 R-01).</summary>
    public int Version { get; private set; }

    /// <returns><c>true</c> si la jornada o la hora cambiaron.</returns>
    public bool Reprogramar(int jornada, DateTimeOffset? programadoEn)
    {
        Reglas.ExigirJornada(jornada);
        var hora = Reglas.HoraEnUtc(programadoEn);
        if (jornada == Jornada && hora == ProgramadoEn) return false;
        Jornada = jornada;
        ProgramadoEn = hora;
        return true;
    }

    /// <summary>
    /// Fija la planilla vigente: su número y su estado, siempre juntos (Spec 006 R-07). Solo la planilla lo llama; la
    /// importación nunca cambia la versión (R-30).
    /// </summary>
    public void AplicarVersion(int version, EstadoDePartido estado)
    {
        if (version < 1) throw new ArgumentOutOfRangeException(nameof(version));
        if (estado == EstadoDePartido.Cancelado) throw new ArgumentException("Una planilla no deja el partido cancelado.", nameof(estado));
        Version = version;
        Estado = estado;
    }

    /// <summary>Un partido con resultado (<c>Jugado</c>, <c>Suspendido</c> o <c>Adjudicado</c>) no se cancela (Spec 006 R-27).</summary>
    /// <returns><c>true</c> si el estado cambió.</returns>
    public bool Cancelar() =>
        Estado is EstadoDePartido.Jugado or EstadoDePartido.Suspendido or EstadoDePartido.Adjudicado
            ? throw new ReglaDeDominioException(RazonesDeDominio.PartidoConResultado)
            : Cambiar(EstadoDePartido.Cancelado);

    /// <summary>
    /// Solo un partido <c>Cancelado</c> vuelve a <c>Programado</c> (Spec 006 R-26): cualquier otro estado, incluido un
    /// resultado, queda como está.
    /// </summary>
    /// <returns><c>true</c> si el estado cambió.</returns>
    public bool Reactivar() => Estado == EstadoDePartido.Cancelado && Cambiar(EstadoDePartido.Programado);

    private bool Cambiar(EstadoDePartido estado)
    {
        if (Estado == estado) return false;
        Estado = estado;
        return true;
    }
}
