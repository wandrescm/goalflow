using GoalFlow.Application.Persistencia;

namespace GoalFlow.Application.Torneos;

/// <summary>Lectura pública de los equipos del tenant actual.</summary>
public class ConsultaDeEquipos(IUnidadDeTrabajo unidadDeTrabajo, ILectorDeEquipos lector)
{
    public async Task<IReadOnlyList<EquipoResumen>> EjecutarAsync(CancellationToken cancellationToken)
    {
        await using var transaccion = await unidadDeTrabajo.IniciarAsync(cancellationToken);
        var equipos = await lector.ListarAsync(cancellationToken);
        await transaccion.ConfirmarAsync(cancellationToken);
        return equipos;
    }
}
