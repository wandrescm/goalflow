using GoalFlow.Application.Estadisticas.Calculo;
using GoalFlow.Application.Persistencia;
using GoalFlow.Application.Tenancy;
using GoalFlow.Application.Torneos.Contracts;
using Microsoft.Extensions.Logging;

namespace GoalFlow.Application.Estadisticas;

public sealed record TorneoDeRespuesta(string Codigo, string Nombre);

/// <summary>El reglamento vigente de la respuesta (Spec 009 R-27): una versión <c>Provisional</c> se devuelve como tal.</summary>
public sealed record ReglamentoDeRespuesta(int Version, string Estado);

public sealed record FaseDeRespuesta(int Orden);

public sealed record RespuestaDePosiciones(
    TorneoDeRespuesta Torneo,
    ReglamentoDeRespuesta? Reglamento,
    FaseDeRespuesta Fase,
    IReadOnlyList<FilaDePosicion> Filas,
    IReadOnlyList<EmpateSinResolver> Empates,
    CoberturaDeTarjetas Cobertura);

public sealed record RespuestaDeGoleadores(
    TorneoDeRespuesta Torneo, ReglamentoDeRespuesta? Reglamento, IReadOnlyList<FilaDeGoleador> Filas, CoberturaDeGoleadores Cobertura);

public sealed record RespuestaDeValla(TorneoDeRespuesta Torneo, ReglamentoDeRespuesta? Reglamento, IReadOnlyList<FilaDeValla> Filas);

public enum EstadoDeConsulta
{
    Ok,
    NoEncontrado,
    ConfiguracionPendiente,
}

/// <summary>El resultado de un caso de uso de lectura. <c>Faltantes</c> solo existe con <see cref="EstadoDeConsulta.ConfiguracionPendiente"/> (R-25).</summary>
public sealed record ResultadoDeConsulta<T>(EstadoDeConsulta Estado, T? Valor, IReadOnlyList<string> Faltantes)
    where T : class
{
    public static ResultadoDeConsulta<T> Ok(T valor) => new(EstadoDeConsulta.Ok, valor, []);

    public static ResultadoDeConsulta<T> NoEncontrado() => new(EstadoDeConsulta.NoEncontrado, null, []);

    public static ResultadoDeConsulta<T> Pendiente(IReadOnlyList<string> faltantes) => new(EstadoDeConsulta.ConfiguracionPendiente, null, faltantes);
}

/// <summary>
/// Qué configuración del reglamento necesita cada tabla (Spec 009 R-25). Nunca se asume un valor: lo que falta se lista, en el
/// orden <c>puntos</c>, <c>desempate</c>, <c>adjudicacion.&lt;M&gt;</c> (motivos en orden ordinal).
/// </summary>
internal static class ConfiguracionRequerida
{
    public static List<string> ParaPosiciones(DatosDeTorneoParaEstadisticas datos, int faseOrden)
    {
        var faltantes = new List<string>();
        if (datos.Reglamento?.Puntos is null) faltantes.Add("puntos");
        if (datos.Reglamento?.Desempate is null) faltantes.Add("desempate");
        faltantes.AddRange(Adjudicaciones(datos, p => p.FaseOrden == faseOrden));
        // R-25: el orden es puntos, desempate y después las adjudicaciones, en orden ordinal.
        return faltantes;
    }

    /// <summary>Los goleadores necesitan la adjudicación solo de los partidos adjudicados con marcador deportivo.</summary>
    public static List<string> ParaGoleadores(DatosDeTorneoParaEstadisticas datos) =>
        Adjudicaciones(datos, p => p.Planilla?.MarcadorLocal is not null);

    public static List<string> ParaValla(DatosDeTorneoParaEstadisticas datos) => Adjudicaciones(datos, _ => true);

    private static List<string> Adjudicaciones(DatosDeTorneoParaEstadisticas datos, Func<PartidoParaEstadisticas, bool> filtro) =>
    [
        .. datos.Partidos
            .Where(p => p.Estado == EstadoDePartidoParaEstadisticas.Adjudicado && filtro(p))
            .Select(p => p.Planilla?.Motivo)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Where(m => datos.Reglamento?.Adjudicacion is not { } porMotivo || !porMotivo.ContainsKey(m))
            .Order(StringComparer.Ordinal)
            .Select(m => $"adjudicacion.{m}"),
    ];
}

/// <summary>Lo común de los tres casos de uso: la foto consistente (R-11), la configuración pendiente (R-25) y el registro (R-35).</summary>
public abstract class ConsultaDeEstadisticas<T>(
    IUnidadDeTrabajo unidadDeTrabajo, ILectorDeTorneoParaEstadisticas lector, ITenantContext tenantContext, ILogger logger)
    where T : class
{
    protected abstract string Endpoint { get; }

    protected async Task<ResultadoDeConsulta<T>> LeerYCalcularAsync(
        string codigoDeTorneo, int? faseOrden, Func<DatosDeTorneoParaEstadisticas, List<string>> faltantes,
        Func<DatosDeTorneoParaEstadisticas, (T Respuesta, int Filas)> calcular, CancellationToken cancellationToken)
    {
        DatosDeTorneoParaEstadisticas? datos;

        // Una sola foto de la base: REPEATABLE READ, sin escribir nada (R-11). El cálculo corre después, fuera de la transacción.
        await using (var transaccion = await unidadDeTrabajo.IniciarLecturaConsistenteAsync(cancellationToken))
        {
            datos = await lector.LeerAsync(codigoDeTorneo, cancellationToken);
            await transaccion.ConfirmarAsync(cancellationToken);
        }

        if (datos is null || (faseOrden is { } orden && datos.Fases.All(f => f.Orden != orden)))
            return ResultadoDeConsulta<T>.NoEncontrado();

        var pendientes = faltantes(datos);
        if (pendientes.Count > 0)
        {
            Registrar(codigoDeTorneo, datos.Reglamento?.Version, 0, "configuracion_pendiente");
            return ResultadoDeConsulta<T>.Pendiente(pendientes);
        }

        var (respuesta, filas) = calcular(datos);
        Registrar(codigoDeTorneo, datos.Reglamento?.Version, filas, "ok");
        return ResultadoDeConsulta<T>.Ok(respuesta);
    }

    protected static TorneoDeRespuesta Torneo(DatosDeTorneoParaEstadisticas datos) => new(datos.Codigo, datos.Nombre);

    protected static ReglamentoDeRespuesta? Reglamento(DatosDeTorneoParaEstadisticas datos) =>
        datos.Reglamento is { } r ? new ReglamentoDeRespuesta(r.Version, r.Estado) : null;

    // Nunca nombres de jugadores ni de equipos (R-35).
    private void Registrar(string codigoDeTorneo, int? versionDelReglamento, int filas, string resultado) =>
        logger.LogInformation(
            "Consulta de estadísticas: liga {Liga}, torneo {Torneo}, endpoint {Endpoint}, reglamento versión {VersionDelReglamento}, filas {Filas}, resultado {Resultado}",
            tenantContext.Current.Value, codigoDeTorneo, Endpoint, versionDelReglamento?.ToString() ?? "ninguno", filas, resultado);
}

public sealed class ConsultaDePosiciones(
    IUnidadDeTrabajo unidadDeTrabajo, ILectorDeTorneoParaEstadisticas lector, ITenantContext tenantContext, ILogger<ConsultaDePosiciones> logger)
    : ConsultaDeEstadisticas<RespuestaDePosiciones>(unidadDeTrabajo, lector, tenantContext, logger)
{
    protected override string Endpoint => "posiciones";

    public Task<ResultadoDeConsulta<RespuestaDePosiciones>> EjecutarAsync(string codigoDeTorneo, int faseOrden, CancellationToken cancellationToken) =>
        LeerYCalcularAsync(codigoDeTorneo, faseOrden, d => ConfiguracionRequerida.ParaPosiciones(d, faseOrden), d =>
        {
            var r = CalculadoraDePosiciones.Calcular(d, faseOrden);
            return (new RespuestaDePosiciones(Torneo(d), Reglamento(d), new FaseDeRespuesta(faseOrden), r.Filas, r.Empates, r.Cobertura), r.Filas.Count);
        }, cancellationToken);
}

public sealed class ConsultaDeGoleadores(
    IUnidadDeTrabajo unidadDeTrabajo, ILectorDeTorneoParaEstadisticas lector, ITenantContext tenantContext, ILogger<ConsultaDeGoleadores> logger)
    : ConsultaDeEstadisticas<RespuestaDeGoleadores>(unidadDeTrabajo, lector, tenantContext, logger)
{
    protected override string Endpoint => "goleadores";

    public Task<ResultadoDeConsulta<RespuestaDeGoleadores>> EjecutarAsync(string codigoDeTorneo, CancellationToken cancellationToken) =>
        LeerYCalcularAsync(codigoDeTorneo, null, ConfiguracionRequerida.ParaGoleadores, d =>
        {
            var r = CalculadoraDeGoleadores.Calcular(d);
            return (new RespuestaDeGoleadores(Torneo(d), Reglamento(d), r.Filas, r.Cobertura), r.Filas.Count);
        }, cancellationToken);
}

public sealed class ConsultaDeValla(
    IUnidadDeTrabajo unidadDeTrabajo, ILectorDeTorneoParaEstadisticas lector, ITenantContext tenantContext, ILogger<ConsultaDeValla> logger)
    : ConsultaDeEstadisticas<RespuestaDeValla>(unidadDeTrabajo, lector, tenantContext, logger)
{
    protected override string Endpoint => "valla";

    public Task<ResultadoDeConsulta<RespuestaDeValla>> EjecutarAsync(string codigoDeTorneo, CancellationToken cancellationToken) =>
        LeerYCalcularAsync(codigoDeTorneo, null, ConfiguracionRequerida.ParaValla, d =>
        {
            var r = CalculadoraDeValla.Calcular(d);
            return (new RespuestaDeValla(Torneo(d), Reglamento(d), r.Filas), r.Filas.Count);
        }, cancellationToken);
}
