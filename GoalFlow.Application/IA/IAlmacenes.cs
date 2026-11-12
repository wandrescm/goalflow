using GoalFlow.Domain.IA;

namespace GoalFlow.Application.IA;

/// <summary>La clave (tenant, partido, huella, número) ya existe: dos reservas de la misma huella chocaron (Spec 007 R-19 c).</summary>
public class NumeroDeLlamadaDuplicadoException(Exception? interna = null)
    : Exception("Ya existe una llamada con ese número para la huella.", interna);

/// <summary>
/// Las llamadas pagadas de la liga del scope (Spec 007 R-02, R-19, R-23). Toda operación exige una transacción activa y lanza
/// <see cref="GoalFlow.Application.Persistencia.TransaccionRequeridaException"/> si no la hay (R-26).
/// </summary>
public interface IAlmacenDeLlamadas
{
    /// <summary>Las filas guardadas de (partido, huella): el tope de llamadas se cuenta en la base (R-17).</summary>
    Task<int> ContarAsync(Guid partidoId, string huella, CancellationToken cancellationToken);

    /// <summary>Inserta la fila <c>Reservada</c>. Traduce solo el 23505 del índice del número a <see cref="NumeroDeLlamadaDuplicadoException"/>.</summary>
    Task InsertarReservadaAsync(LlamadaIa llamada, CancellationToken cancellationToken);

    /// <summary>
    /// Concilia la llamada (R-23): un <c>ExecuteUpdateAsync</c> condicionado a <c>Reservada</c> y, solo si afectó una fila, otro
    /// sobre el contador del mes de la llamada. Si el costo excede la reserva, se registra completo y se marca el exceso.
    /// Devuelve false si no concilió (ya estaba conciliada o no existe).
    /// </summary>
    Task<bool> ConciliarAsync(
        Guid llamadaId, int tokensEntrada, int tokensSalida, long costo, string codigoDeResultado, CancellationToken cancellationToken);
}

/// <summary>Los resúmenes guardados de la liga del scope (Spec 007 R-01). Inmutables: solo se leen e insertan (R-04).</summary>
public interface IAlmacenDeResumenes
{
    Task<bool> ExisteAsync(Guid partidoId, string huella, CancellationToken cancellationToken);

    /// <summary>
    /// Inserta el resumen. Devuelve false si ya existía uno para (partido, huella): traduce solo el 23505 de ese índice
    /// (R-22), y la transacción sigue usable.
    /// </summary>
    Task<bool> InsertarAsync(ResumenPartido resumen, CancellationToken cancellationToken);
}

/// <summary>Quién es visible: el resumen cuya huella es la de la entrada vigente (ADR-12 §2.3), nunca por versión.</summary>
public static class VisibilidadDeResumen
{
    public static ResumenPartido? Seleccionar(IEnumerable<ResumenPartido> resumenes, string huellaVigente) =>
        resumenes.FirstOrDefault(r => r.Huella == huellaVigente);
}
