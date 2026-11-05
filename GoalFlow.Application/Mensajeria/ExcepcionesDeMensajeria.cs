namespace GoalFlow.Application.Mensajeria;

/// <summary>
/// Ya existe una entrega con la misma clave lógica. La lanza el SaveChanges del productor, con la
/// excepción original como interna (Spec 004 R-09).
/// </summary>
public class EntregaDuplicadaException(Exception original)
    : Exception("Ya existe una entrega con la misma clave lógica.", original);

/// <summary>
/// El consumidor declara que reintentar no ayuda. El código de razón lo elige el consumidor entre sus
/// constantes; nunca se guarda un mensaje de excepción (Spec 004 R-14).
/// </summary>
public class FalloPermanenteException(string codigoDeRazon) : Exception("Fallo permanente del consumidor.")
{
    public string CodigoDeRazon { get; } = codigoDeRazon;
}
