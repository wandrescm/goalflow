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

    /// <returns><c>true</c> si el estado cambió.</returns>
    public bool Cancelar() => Cambiar(EstadoDePartido.Cancelado);

    /// <returns><c>true</c> si el estado cambió.</returns>
    public bool Reactivar() => Cambiar(EstadoDePartido.Programado);

    private bool Cambiar(EstadoDePartido estado)
    {
        if (Estado == estado) return false;
        Estado = estado;
        return true;
    }
}
