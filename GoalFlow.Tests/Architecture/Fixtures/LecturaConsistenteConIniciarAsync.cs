// Canario A de la Spec 011 (T-28 provisional, R-20): un método async que abre la lectura consistente y también IniciarAsync.
// Vive en su propio namespace: ninguna otra prueba recorre este namespace.
using GoalFlow.Application.Persistencia;

namespace GoalFlow.Tests.Architecture.Fixtures.LecturaConsistente.ConIniciarAsync
{
    public sealed class CasoConEscritura(IUnidadDeTrabajo unidadDeTrabajo)
    {
        public async Task EjecutarAsync(CancellationToken cancellationToken)
        {
            await using var lectura = await unidadDeTrabajo.IniciarLecturaConsistenteAsync(cancellationToken);
            await using var escritura = await unidadDeTrabajo.IniciarAsync(cancellationToken);
            await lectura.ConfirmarAsync(cancellationToken);
        }
    }
}
