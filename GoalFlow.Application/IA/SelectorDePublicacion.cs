using GoalFlow.Application.IA.Verificacion;
using GoalFlow.Domain.IA;

namespace GoalFlow.Application.IA;

/// <summary>
/// La única regla de publicación que puede usar una ruta de lectura (Spec 008 R-17, R-18; H19). Es pura: la publicación no se
/// guarda, se deriva en cada lectura. Vive aquí y no en <c>Verificacion</c> porque lee <see cref="ResumenPartido"/>, un tipo de
/// dominio que R-21 mantiene fuera del namespace del verificador (C-14).
/// </summary>
public static class SelectorDePublicacion
{
    /// <summary>
    /// El resumen con la huella de <paramref name="vigente"/> (<see cref="VisibilidadDeResumen"/>) que además no es un borrador y
    /// que el verificador actual aprueba contra <paramref name="vigente"/>; si no, <c>null</c> y el lector muestra los hechos de la planilla.
    /// </summary>
    public static ResumenPartido? Seleccionar(IEnumerable<ResumenPartido> resumenes, EntradaCanonica vigente)
    {
        var candidato = VisibilidadDeResumen.Seleccionar(resumenes, vigente.Huella());
        return candidato is { Estado: EstadoDeResumen.PorVerificar } && VerificadorDeResumen.Verificar(candidato.Texto, vigente).Aprobado
            ? candidato
            : null;
    }
}
