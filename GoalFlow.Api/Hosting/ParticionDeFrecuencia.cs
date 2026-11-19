using System.Net;
using System.Net.Sockets;

namespace GoalFlow.Api.Hosting;

/// <summary>
/// Función de partición del limitador (Spec 010 R-08, R-09). Es el único tipo de producción que nombra el encabezado
/// <c>CF-Connecting-IP</c> y que decide qué IP es la del cliente (R-22, C-16). Nada lee <c>ConnectionInfo.RemoteIpAddress</c>.
/// </summary>
public static class ParticionDeFrecuencia
{
    /// <summary>Clave compartida de toda petición sin IP de cliente utilizable (R-08).</summary>
    public const string SinIp = "sin-ip";

    private const string EncabezadoDelCliente = "CF-Connecting-IP";

    /// <summary>
    /// La IP normalizada del cliente, o <see cref="SinIp"/>. Solo vale con exactamente una instancia del encabezado y un literal IP
    /// válido: IPv4 tal cual, IPv4 mapeada como su IPv4, otra IPv6 como su /64 en forma canónica.
    /// </summary>
    public static string ClaveDelCliente(HttpRequest peticion)
    {
        var instancias = peticion.Headers[EncabezadoDelCliente];
        if (instancias.Count != 1) return SinIp;
        if (!IPAddress.TryParse(instancias[0]?.Trim(), out var ip)) return SinIp;

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv4MappedToIPv6) return ip.MapToIPv4().ToString();

            Span<byte> bytes = stackalloc byte[16];
            ip.TryWriteBytes(bytes, out _);
            bytes[8..].Clear();
            return new IPAddress(bytes).ToString();
        }

        return ip.ToString();
    }

    /// <summary>La clave de partición del limitador por cliente: <c>{clase}|{ip-normalizada o sin-ip}</c>.</summary>
    public static string ClaveDeParticion(HttpContext contexto) =>
        $"{ClaseDe(contexto.Request.Path)}|{ClaveDelCliente(contexto.Request)}";

    /// <summary><c>salud</c> para /health; <c>admin</c> si el segundo segmento es admin; <c>publica</c> para todo lo demás (R-09).</summary>
    public static ClaseDeRuta ClaseDe(PathString ruta)
    {
        var segmentos = (ruta.Value ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segmentos.Length == 1 && string.Equals(segmentos[0], "health", StringComparison.OrdinalIgnoreCase)) return ClaseDeRuta.Salud;
        if (segmentos.Length >= 2 && string.Equals(segmentos[1], "admin", StringComparison.OrdinalIgnoreCase)) return ClaseDeRuta.Admin;
        return ClaseDeRuta.Publica;
    }
}
