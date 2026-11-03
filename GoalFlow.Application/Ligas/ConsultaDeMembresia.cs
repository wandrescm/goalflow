using GoalFlow.Application.Persistencia;

namespace GoalFlow.Application.Ligas;

/// <summary>Membresía vigente: se lee en cada petición de Admin, sin caché (Spec 003 R-11, R-12).</summary>
public class ConsultaDeMembresia(IUnidadDeTrabajo unidadDeTrabajo, ILectorDeMembresia lector)
{
    public async Task<string?> EjecutarAsync(CancellationToken cancellationToken)
    {
        await using var transaccion = await unidadDeTrabajo.IniciarAsync(cancellationToken);
        var rol = await lector.ObtenerRolAsync(cancellationToken);
        await transaccion.ConfirmarAsync(cancellationToken);
        return rol;
    }
}
