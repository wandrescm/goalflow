namespace GoalFlow.Application.IA;

/// <summary>Los tokens que el proveedor reportó en su respuesta (<c>usage</c>).</summary>
public sealed record UsoReportado(int TokensEntrada, int TokensSalida);

/// <summary>
/// La solicitud exacta, igual para contar y para generar (Spec 007 R-28): el texto de sistema, la entrada canónica (que viaja
/// solo en el bloque de datos del mensaje de usuario), el límite de salida y el ajuste de razonamiento.
/// </summary>
public sealed record SolicitudDeGeneracion(
    string TextoDeSistema, string EntradaCanonica, int MaxTokensDeSalida, RazonamientoDeIA Razonamiento);

/// <summary>La respuesta: el texto (los bloques de tipo texto concatenados), el <c>stop_reason</c> y el uso facturable.</summary>
public sealed record RespuestaGenerada(string Texto, string StopReason, int TokensEntrada, int TokensSalida);

/// <summary>
/// El proveedor de texto detrás de un puerto (ADR-12 §2.1). Ambas operaciones lanzan una de tres excepciones específicas:
/// <see cref="RespuestaConErrorException"/>, <see cref="ResultadoInciertoException"/> o <see cref="SalidaInvalidaException"/>.
/// </summary>
public interface IGeneradorDeTexto
{
    /// <summary>Cuenta los tokens de entrada de la solicitud exacta con el endpoint gratuito: una estimación (R-12).</summary>
    Task<int> ContarAsync(SolicitudDeGeneracion solicitud, CancellationToken cancellationToken);

    /// <summary>Genera el texto. Es la única llamada pagada.</summary>
    Task<RespuestaGenerada> GenerarAsync(SolicitudDeGeneracion solicitud, CancellationToken cancellationToken);
}

/// <summary>El proveedor respondió con un estado de error. Lleva el uso reportado, si lo hubo.</summary>
public class RespuestaConErrorException(int estado, UsoReportado? uso, string? mensaje = null)
    : Exception(mensaje ?? "El proveedor respondió con un error.")
{
    public int Estado { get; } = estado;

    public UsoReportado? Uso { get; } = uso;
}

/// <summary>No hubo respuesta: tiempo agotado, conexión perdida o una excepción sin respuesta. Pudo cobrarse (R-21 f).</summary>
public class ResultadoInciertoException(string? mensaje = null, Exception? interna = null)
    : Exception(mensaje ?? "No hubo respuesta del proveedor.", interna);

/// <summary>El proveedor respondió con éxito pero el cuerpo no se pudo interpretar. Lleva el uso reportado, si lo hubo.</summary>
public class SalidaInvalidaException(UsoReportado? uso, string? mensaje = null)
    : Exception(mensaje ?? "La respuesta del proveedor no tiene la forma esperada.")
{
    public UsoReportado? Uso { get; } = uso;
}

/// <summary>
/// Un fallo que el outbox reintenta con su backoff (ADR-02): el código de razón es estable y no lleva datos del usuario.
/// El ciclo guarda solo el nombre del tipo; el código queda en el registro estructurado del consumidor.
/// </summary>
public class FalloReintentableException(string codigoDeRazon, Exception? interna = null)
    : Exception("Fallo reintentable del consumidor.", interna)
{
    public string CodigoDeRazon { get; } = codigoDeRazon;
}
