// Canarios de la Spec 010 (T-21, R-22): tipos que hacen lo que la regla prohíbe. Viven en su propio namespace: ninguna otra prueba
// recorre este namespace, y la regla de producción no los ve porque solo recorre los ensamblados de producción.
namespace GoalFlow.Tests.Architecture.Fixtures.IpDelCliente
{
    /// <summary>Lee el encabezado por su nombre: viola (a).</summary>
    public static class LecturaDeEncabezado
    {
        public static string? Leer(Microsoft.AspNetCore.Http.HttpRequest peticion) => peticion.Headers["CF-Connecting-IP"];
    }

    /// <summary>Lee <c>RemoteIpAddress</c>: viola (b).</summary>
    public static class LecturaDeRemoteIp
    {
        public static System.Net.IPAddress? Leer(Microsoft.AspNetCore.Http.HttpContext contexto) => contexto.Connection.RemoteIpAddress;
    }
}
