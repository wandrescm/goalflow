// Canario B de la Spec 011 (T-28 provisional, R-20): un método async que solo llama a IniciarAsync (READ COMMITTED).
// Vive en su propio namespace: ninguna otra prueba recorre este namespace.
using GoalFlow.Application.Persistencia;

namespace GoalFlow.Tests.Architecture.Fixtures.LecturaConsistente.SinLectura
{
    public sealed class CasoSinLectura(IUnidadDeTrabajo unidadDeTrabajo)
    {
        public async Task EjecutarAsync(CancellationToken cancellationToken)
        {
            await using var transaccion = await unidadDeTrabajo.IniciarAsync(cancellationToken);
            await transaccion.ConfirmarAsync(cancellationToken);
        }
    }
}
