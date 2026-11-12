namespace GoalFlow.Application.IA;

/// <summary>
/// El desenlace de una respuesta con error del proveedor (Spec 007 v3 R-21, ADR-12 §2.6). Cada valor dice qué se escribe en el libro
/// de presupuesto y qué le pasa a la entrega.
/// </summary>
public enum DesenlaceDeError
{
    /// <summary>(c) Cierto. Se concilia con el uso reportado (o 0) y el fallo es reintentable.</summary>
    Reintentable,

    /// <summary>(d) Cierto. Se concilia (0 si no hubo uso), se guarda el borrador <c>solicitud_invalida</c> y se completa.</summary>
    BorradorPorSolicitudInvalida,

    /// <summary>(e) Cierto. Se concilia en 0 y el fallo es permanente <c>credencial_invalida</c>.</summary>
    CredencialInvalida,

    /// <summary>(e2) Cierto. Se concilia en 0 y el fallo es permanente <c>facturacion_del_proveedor</c>.</summary>
    FacturacionDelProveedor,

    /// <summary>(f) Incierto: pudo cobrarse. No se escribe nada en la llamada ni en el contador (aunque haya habido respuesta) y el fallo es reintentable.</summary>
    Incierto,
}

/// <summary>
/// Clasifica por el estado HTTP y por el <c>error.type</c> documentado, <b>nunca por el mensaje</b> (ADR-12 C-23). Función pura.
/// </summary>
public static class ClasificadorDeErrorDelProveedor
{
    public const string CodigoDeCredencialInvalida = "credencial_invalida";
    public const string CodigoDeFacturacionDelProveedor = "facturacion_del_proveedor";

    public static DesenlaceDeError Clasificar(RespuestaConErrorException error) => Clasificar(error.Estado, error.TipoDeError);

    public static DesenlaceDeError Clasificar(int estado, string tipoDeError)
    {
        // Un timeout que informa el servidor, sea cual sea el estado, es incierto: el proveedor puede facturar una solicitud agotada (ADR-12 hecho 8).
        if (tipoDeError == TiposDeErrorDelProveedor.TiempoAgotado) return DesenlaceDeError.Incierto;

        return estado switch
        {
            400 or 404 or 413 => DesenlaceDeError.BorradorPorSolicitudInvalida, // un 400 incluye un tope de gasto o un saldo agotado (hecho 9)
            401 or 403 => DesenlaceDeError.CredencialInvalida,
            402 => DesenlaceDeError.FacturacionDelProveedor,
            409 or 429 or 500 or 529 => DesenlaceDeError.Reintentable, // el 500 con timeout_error ya salió arriba
            504 => DesenlaceDeError.Incierto,
            >= 500 and <= 599 => DesenlaceDeError.Reintentable,
            _ => DesenlaceDeError.Incierto, // cualquier otro estado, incluido un 4xx desconocido: se trata como posiblemente facturado
        };
    }

    /// <summary>
    /// El código de resultado que se registra: el estado HTTP y, si es un valor documentado, el <c>error.type</c> (R-27). Nunca el mensaje.
    /// Ejemplos: <c>http_429_rate_limit_error</c>, <c>http_418</c>.
    /// </summary>
    public static string CodigoDeResultado(RespuestaConErrorException error) =>
        error.TipoDeError == TiposDeErrorDelProveedor.Desconocido ? $"http_{error.Estado}" : $"http_{error.Estado}_{error.TipoDeError}";
}
