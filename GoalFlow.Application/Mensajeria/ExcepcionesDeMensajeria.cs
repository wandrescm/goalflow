using GoalFlow.Domain.Tenancy;

namespace GoalFlow.Application.Mensajeria;

/// <summary>La clave lógica de una fila de entrega: (tenant, evento, agregado, versión, consumidor). Spec 004 R-03.</summary>
public sealed record ClaveLogicaDeEntrega(TenantId TenantId, string TipoDeEvento, Guid AgregadoId, int Version, string Consumidor);

/// <summary>
/// Ya existe una entrega con la misma clave lógica. La lanza el SaveChanges del productor, con la
/// excepción original como interna (Spec 004 R-09).
/// </summary>
/// <param name="original">La excepción de la base.</param>
/// <param name="claves">
/// Las claves lógicas de las filas de entrega pendientes en el guardado que falló (Spec 006 R-20): permiten a un productor
/// con versión optimista reconocer su propio duplicado. Vacía si no se informan.
/// </param>
public class EntregaDuplicadaException(Exception original, IReadOnlyList<ClaveLogicaDeEntrega>? claves = null)
    : Exception("Ya existe una entrega con la misma clave lógica.", original)
{
    public IReadOnlyList<ClaveLogicaDeEntrega> Claves { get; } = claves ?? [];
}

/// <summary>
/// El consumidor declara que reintentar no ayuda. El código de razón lo elige el consumidor entre sus
/// constantes; nunca se guarda un mensaje de excepción (Spec 004 R-14).
/// </summary>
public class FalloPermanenteException(string codigoDeRazon, Exception? interna = null)
    : Exception("Fallo permanente del consumidor.", interna)
{
    public string CodigoDeRazon { get; } = codigoDeRazon;
}

/// <summary>
/// La publicación dirigida (Spec 007 R-31) nombró un consumidor que no está registrado para ese tipo de evento en este host.
/// Lleva el nombre del consumidor, que es un código estable y no un dato del usuario.
/// </summary>
public class ConsumidorNoRegistradoException(string consumidor)
    : Exception("El consumidor no está registrado para ese evento en este host.")
{
    public string Consumidor { get; } = consumidor;
}
