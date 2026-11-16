namespace GoalFlow.Api.Hosting;

/// <summary>
/// <c>GET /{slug}/admin/diagnostico/red</c> (Spec 010 Parte A, R-01 a R-06). Diagnóstico temporal solo para el Admin: se mapea dentro
/// del grupo de Admin y solo si <c>Diagnostico:RedHabilitado</c> es <c>true</c> al arrancar. Apagado o ausente, la ruta no existe y
/// responde <c>404</c> a cualquiera. La Parte B de la Spec lo elimina.
/// </summary>
public static class DiagnosticoDeRed
{
    public const string ClaveDelInterruptor = "Diagnostico:RedHabilitado";

    /// <summary>El interruptor se lee al arrancar. Ausente o cualquier valor que no sea <c>true</c> es apagado.</summary>
    public static bool Habilitado(IConfiguration configuracion) => configuracion.GetValue<bool>(ClaveDelInterruptor);

    /// <summary>Registra la captura. Va inmediatamente antes de <c>UseGoalFlowForwardedHeaders</c> y solo con el interruptor en true.</summary>
    public static IApplicationBuilder UseGoalFlowDiagnosticoDeRed(this IApplicationBuilder app) =>
        app.UseMiddleware<CapturaDeRedMiddleware>();

    public static RouteGroupBuilder MapDiagnosticoDeRed(this RouteGroupBuilder admin)
    {
        admin.MapGet("/diagnostico/red", (HttpContext contexto) => Results.Ok(ArmarCuerpo(contexto)));

        return admin;
    }

    /// <summary>La ruta del diagnóstico: 4 segmentos con <c>admin</c>, <c>diagnostico</c> y <c>red</c> en las posiciones 2 a 4, sin distinguir mayúsculas.</summary>
    internal static bool EsLaRuta(PathString ruta)
    {
        if (string.IsNullOrEmpty(ruta.Value)) return false;

        var segmentos = ruta.Value.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segmentos.Length == 4
            && string.Equals(segmentos[1], "admin", StringComparison.OrdinalIgnoreCase)
            && string.Equals(segmentos[2], "diagnostico", StringComparison.OrdinalIgnoreCase)
            && string.Equals(segmentos[3], "red", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>La lista cerrada de R-02: ningún otro encabezado se lee, y menos aún Authorization o Cookie (R-04).</summary>
    private static readonly string[] EncabezadosDeLaLista =
    [
        "X-Forwarded-For", "X-Forwarded-Proto", "Forwarded", "X-Real-IP",
        "CF-Connecting-IP", "CF-Connecting-IPv6", "CF-Pseudo-IPv4", "True-Client-IP",
    ];

    private const int MaximoDeInstancias = 8;
    private const int MaximoDeCaracteres = 512;
    private const string XForwardedFor = "X-Forwarded-For";

    internal static CapturaDeRed Capturar(HttpContext contexto)
    {
        var encabezados = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var nombre in EncabezadosDeLaLista)
        {
            if (!contexto.Request.Headers.TryGetValue(nombre, out var instancias) || instancias.Count == 0) continue;

            encabezados[nombre] =
            [
                .. instancias
                    .Where(valor => valor is not null)
                    .Take(MaximoDeInstancias)
                    .Select(valor => valor!.Length > MaximoDeCaracteres ? valor[..MaximoDeCaracteres] : valor),
            ];
        }

        return new CapturaDeRed(contexto.Connection.RemoteIpAddress?.ToString(), encabezados);
    }

    internal static CuerpoDeRed ArmarCuerpo(HttpContext contexto)
    {
        var captura = contexto.Features.Get<CapturaDeRed>()
            ?? throw new InvalidOperationException("The network capture middleware did not run for the diagnostic route.");

        string[] entradas = captura.Encabezados.TryGetValue(XForwardedFor, out var instancias)
            ? [.. instancias.SelectMany(instancia => instancia.Split(',')).Select(entrada => entrada.Trim())]
            : [];

        contexto.Response.Headers.CacheControl = "no-store";

        return new CuerpoDeRed(
            new AntesDeRed(captura.IpDeConexion, captura.Encabezados),
            entradas,
            new DespuesDeRed(contexto.Connection.RemoteIpAddress?.ToString(), contexto.Request.Scheme));
    }
}

/// <summary>Lo que llegó antes del middleware de encabezados reenviados: la IP de la conexión y los encabezados de la lista cerrada (R-02).</summary>
public sealed record CapturaDeRed(string? IpDeConexion, IReadOnlyDictionary<string, string[]> Encabezados);

public sealed record CuerpoDeRed(AntesDeRed Antes, string[] XForwardedForEntradas, DespuesDeRed Despues);

public sealed record AntesDeRed(string? IpDeConexion, IReadOnlyDictionary<string, string[]> Encabezados);

public sealed record DespuesDeRed(string? IpRemota, string Esquema);

/// <summary>Toma la captura de R-02 para las peticiones de la ruta del diagnóstico; para cualquier otra no hace nada. No escribe logs (R-05).</summary>
public sealed class CapturaDeRedMiddleware(RequestDelegate siguiente)
{
    public Task InvokeAsync(HttpContext contexto)
    {
        if (DiagnosticoDeRed.EsLaRuta(contexto.Request.Path))
            contexto.Features.Set(DiagnosticoDeRed.Capturar(contexto));

        return siguiente(contexto);
    }
}
