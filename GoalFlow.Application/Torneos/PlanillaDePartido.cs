using GoalFlow.Application.Mensajeria;
using GoalFlow.Application.Persistencia;
using GoalFlow.Application.Tenancy;
using GoalFlow.Application.Torneos.Contracts;
using GoalFlow.Domain.Torneos;
using Microsoft.Extensions.Logging;

namespace GoalFlow.Application.Torneos;

/// <summary>Clave natural de un partido en la liga del llamante (Spec 006 R-08).</summary>
public sealed record ClaveDePartido(string Torneo, int Fase, string Local, string Visitante);

/// <summary>Un jugador inscrito en el torneo con el equipo local o visitante del partido.</summary>
public sealed record JugadorInscrito(string Codigo, Guid InscripcionId, LadoDelPartido Lado);

/// <summary>Lo que el guardado lee, y nada más (Spec 006 §4): el partido, su planilla vigente y las inscripciones de sus dos equipos.</summary>
public sealed record PartidoCargado(
    Partido Partido,
    Planilla? Vigente,
    IReadOnlyList<Gol> Goles,
    IReadOnlyList<Tarjeta> Tarjetas,
    IReadOnlyList<JugadorInscrito> Jugadores);

/// <summary>Puerto de almacenamiento de la planilla. Corre dentro de la transacción de <see cref="IUnidadDeTrabajo"/>.</summary>
public interface IAlmacenDePlanilla
{
    /// <returns>El partido con su planilla vigente, o <c>null</c> si no existe en la liga del llamante.</returns>
    Task<PartidoCargado?> CargarAsync(ClaveDePartido clave, CancellationToken cancellationToken);

    void AgregarVersion(Planilla planilla, IReadOnlyList<Gol> goles, IReadOnlyList<Tarjeta> tarjetas);

    /// <exception cref="ConflictoDeVersionException">Otra transacción cambió el partido o escribió esa versión primero (R-20).</exception>
    Task GuardarAsync(CancellationToken cancellationToken);
}

/// <summary>El partido no existe en la liga del llamante (R-08).</summary>
public class PartidoInexistenteException() : Exception("El partido no existe en esta liga.");

/// <summary>La planilla rompe R-11, R-12 o R-14. Lleva todos los errores, con su ruta y su código (R-13).</summary>
public class PlanillaInvalidaException(IReadOnlyList<ErrorDePlanilla> errores) : Exception("La planilla no es válida.")
{
    public IReadOnlyList<ErrorDePlanilla> Errores { get; } = errores;
}

/// <summary>El partido está cancelado y no admite planilla (R-15).</summary>
public class PartidoCanceladoException() : Exception("El partido está cancelado.");

/// <summary>
/// La versión esperada no es la vigente (R-18) o otra transacción ganó la carrera (R-20, C-26). Nunca lleva el nombre de una
/// restricción ni un código de la base. <see cref="VersionActual"/> es <c>null</c> si no se volvió a leer tras el conflicto.
/// </summary>
public class ConflictoDeVersionException(int? versionActual) : Exception("Conflicto de versión de la planilla.")
{
    public int? VersionActual { get; } = versionActual;
}

public sealed record RespuestaDeGuardado(string Resultado, int Version);

/// <summary>La planilla vigente de un partido, con el mismo contorno que el <c>PUT</c> (R-16).</summary>
public sealed record VistaDePlanilla(int Version, PlanillaDescripcion Planilla);

/// <summary>Traduce entre las filas guardadas, la planilla resuelta que se compara y la descripción que sale por el GET.</summary>
internal static class ConversorDePlanilla
{
    public static PlanillaResuelta Vacia(EstadoDePartido estado) =>
        new(estado, null, null, null, null, null, null, null, [], []);

    /// <returns><c>null</c> en la versión 0.</returns>
    public static PlanillaResuelta? Vigente(PartidoCargado c)
    {
        if (c.Vigente is not { } p) return null;
        var codigos = c.Jugadores.ToDictionary(j => j.InscripcionId, j => j.Codigo);

        return new PlanillaResuelta(
            p.Estado, p.MarcadorLocal, p.MarcadorVisitante, p.OficialLocal, p.OficialVisitante,
            p.Motivo, p.MotivoRectificacion, p.TarjetasInformadas,
            [.. c.Goles.Select(g => new FilaDeGol(
                g.Tipo, g.EquipoTorneoId == c.Partido.LocalId ? LadoDelPartido.Local : LadoDelPartido.Visitante,
                g.InscripcionId is { } i ? codigos[i] : null, g.Cantidad))],
            [.. c.Tarjetas.Select(t => new FilaDeTarjeta(codigos[t.InscripcionId], t.Color, t.Cantidad))]);
    }

    public static PlanillaDescripcion Describir(PlanillaResuelta r)
    {
        static MarcadorDescripcion? Par(int? local, int? visitante) => local is null ? null : new(local, visitante);

        GolDeJugadorDescripcion[] Lista(TipoDeGol tipo) =>
        [
            .. r.Goles.Where(g => g.Tipo == tipo)
                .OrderBy(g => g.Jugador, StringComparer.Ordinal)
                .Select(g => new GolDeJugadorDescripcion(g.Jugador!, g.Cantidad)),
        ];

        int SinAutor(LadoDelPartido lado) => r.Goles.Where(g => g.Tipo == TipoDeGol.SinAutor && g.Acreditado == lado).Sum(g => g.Cantidad);

        return new PlanillaDescripcion(
            r.Estado.ToString(),
            Par(r.MarcadorLocal, r.MarcadorVisitante),
            Par(r.OficialLocal, r.OficialVisitante),
            r.Motivo?.ToString(),
            r.MotivoRectificacion?.ToString(),
            r.TarjetasInformadas,
            Lista(TipoDeGol.Jugador),
            Lista(TipoDeGol.Autogol),
            new MarcadorDescripcion(SinAutor(LadoDelPartido.Local), SinAutor(LadoDelPartido.Visitante)),
            [.. r.Tarjetas.OrderBy(t => t.Jugador, StringComparer.Ordinal).ThenBy(t => t.Color)
                .Select(t => new TarjetaDescripcion(t.Jugador, t.Color.ToString(), t.Cantidad))]);
    }
}

/// <summary><c>GET</c> de la planilla vigente (Spec 006 R-16). Con versión 0 devuelve el estado y una planilla vacía.</summary>
public class ConsultaDePlanilla(IUnidadDeTrabajo unidadDeTrabajo, IAlmacenDePlanilla almacen)
{
    /// <exception cref="PartidoInexistenteException">El partido no existe en la liga del llamante.</exception>
    public async Task<VistaDePlanilla> EjecutarAsync(ClaveDePartido clave, CancellationToken cancellationToken)
    {
        await using var transaccion = await unidadDeTrabajo.IniciarAsync(cancellationToken);
        var cargado = await almacen.CargarAsync(clave, cancellationToken) ?? throw new PartidoInexistenteException();

        var vigente = ConversorDePlanilla.Vigente(cargado) ?? ConversorDePlanilla.Vacia(cargado.Partido.Estado);
        var vista = new VistaDePlanilla(cargado.Partido.Version, ConversorDePlanilla.Describir(vigente));
        await transaccion.ConfirmarAsync(cancellationToken);
        return vista;
    }
}

/// <summary>
/// <c>PUT</c> de la planilla completa (Spec 006 R-09 a R-22). El orden de evaluación es el de R-17: 404, 422 (R-11, R-12),
/// 409 (R-15), 200 sinCambios, 409 (R-18), 422 (R-14), guardado (R-19).
/// </summary>
public class GuardarPlanilla(
    IUnidadDeTrabajo unidadDeTrabajo,
    IAlmacenDePlanilla almacen,
    IPublicadorDeEventos publicador,
    ITenantContext tenantContext,
    ILogger<GuardarPlanilla> logger)
{
    public const string Guardada = "guardada";
    public const string SinCambios = "sinCambios";

    /// <param name="administrador">El <c>sub</c> del Admin autenticado: se guarda en la versión y nunca se devuelve (§4).</param>
    public async Task<RespuestaDeGuardado> EjecutarAsync(
        ClaveDePartido clave, Guid administrador, int versionEsperada, PlanillaDescripcion deseada, CancellationToken cancellationToken)
    {
        var tenant = tenantContext.Current;

        await using var transaccion = await unidadDeTrabajo.IniciarAsync(cancellationToken);
        var cargado = await almacen.CargarAsync(clave, cancellationToken) ?? throw new PartidoInexistenteException();
        var partido = cargado.Partido;

        // R-11 y R-12: la planilla se valida por sí misma, sin mirar lo guardado.
        var jugadores = cargado.Jugadores.ToDictionary(j => j.Codigo, j => j.Lado, StringComparer.Ordinal);
        var validacion = ValidadorDePlanilla.Validar(deseada, jugadores);
        if (validacion.Planilla is not { } resuelta) throw new PlanillaInvalidaException(validacion.Errores);

        if (partido.Estado == EstadoDePartido.Cancelado) throw new PartidoCanceladoException();

        // R-17: la igualdad de contenido se decide antes que la versión, así un reintento tras una respuesta perdida es 200.
        var vigente = ConversorDePlanilla.Vigente(cargado);
        var actual = vigente ?? ConversorDePlanilla.Vacia(partido.Estado);
        if (actual.Equivale(resuelta))
        {
            Registrar(tenant.Value, clave, partido.Version, partido.Estado, resuelta.Estado, SinCambios);
            return new RespuestaDeGuardado(SinCambios, partido.Version);
        }

        // R-18, y recién entonces la regla de rectificación (R-14): nunca convierte un reintento ni una petición vieja en 422.
        if (versionEsperada != partido.Version) throw new ConflictoDeVersionException(partido.Version);
        if (!ReglaDeRectificacion.EsValida(partido.Estado, vigente, resuelta))
            throw new PlanillaInvalidaException([new ErrorDePlanilla("motivoRectificacion", CodigosDePlanilla.RectificacionInvalida)]);

        // R-19: una versión nueva con sus filas, el partido actualizado y todo en la misma transacción.
        var estadoAnterior = partido.Estado;
        var version = partido.Version + 1;
        var planilla = new Planilla(
            Guid.CreateVersion7(), partido.TenantId, partido.TorneoId, partido.Id, version, resuelta.Estado,
            resuelta.MarcadorLocal, resuelta.MarcadorVisitante, resuelta.OficialLocal, resuelta.OficialVisitante,
            resuelta.Motivo, resuelta.MotivoRectificacion, resuelta.TarjetasInformadas, administrador);
        var inscripciones = cargado.Jugadores.ToDictionary(j => j.Codigo, j => j.InscripcionId, StringComparer.Ordinal);

        var goles = resuelta.Goles
            .OrderBy(g => g.Tipo).ThenBy(g => g.Acreditado).ThenBy(g => g.Jugador, StringComparer.Ordinal)
            .Select(g => new Gol(
                Guid.CreateVersion7(), partido.TenantId, partido.TorneoId, planilla.Id, g.Tipo,
                g.Acreditado == LadoDelPartido.Local ? partido.LocalId : partido.VisitanteId,
                g.Jugador is null ? null : inscripciones[g.Jugador], g.Cantidad))
            .ToList();
        var tarjetas = resuelta.Tarjetas
            .OrderBy(t => t.Jugador, StringComparer.Ordinal).ThenBy(t => t.Color)
            .Select(t => new Tarjeta(
                Guid.CreateVersion7(), partido.TenantId, partido.TorneoId, planilla.Id, inscripciones[t.Jugador], t.Color, t.Cantidad))
            .ToList();

        partido.AplicarVersion(version, resuelta.Estado);
        almacen.AgregarVersion(planilla, goles, tarjetas);

        // R-24: una vez por versión nueva, dentro de la transacción; sin consumidores registrados no escribe ninguna fila.
        publicador.Publicar(new PartidoGuardado(partido.TenantId, partido.Id, version));
        await almacen.GuardarAsync(cancellationToken);
        await transaccion.ConfirmarAsync(cancellationToken);

        Registrar(tenant.Value, clave, version, estadoAnterior, resuelta.Estado, Guardada);
        return new RespuestaDeGuardado(Guardada, version);
    }

    // R-22: nunca el nombre de un jugador ni de un equipo, ni sus códigos.
    private void Registrar(Guid liga, ClaveDePartido clave, int version, EstadoDePartido desde, EstadoDePartido hacia, string resultado) =>
        logger.LogInformation(
            "Planilla: liga {Liga}, torneo {Torneo}, fase {Fase}, versión {Version}, estado {Desde} a {Hacia}, resultado {Resultado}",
            liga, clave.Torneo, clave.Fase, version, desde, hacia, resultado);
}
