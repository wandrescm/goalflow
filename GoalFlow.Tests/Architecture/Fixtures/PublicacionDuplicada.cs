// Fixture del canario de la Spec 011 (T-05, R-16): un tipo que decide la publicación por su cuenta, filtrando las filas de resumen por
// su estado y consultando la visibilidad. Vive en su propio namespace: ninguna otra prueba recorre este namespace.
using GoalFlow.Application.IA;
using GoalFlow.Domain.IA;

namespace GoalFlow.Tests.Architecture.Fixtures.Publicacion
{
    public static class PublicacionDuplicada
    {
        public static IEnumerable<ResumenPartido> SoloPorVerificar(IEnumerable<ResumenPartido> filas) =>
            filas.Where(r => r.Estado == EstadoDeResumen.PorVerificar);

        public static ResumenPartido? PorHuella(IEnumerable<ResumenPartido> filas, string huella) =>
            VisibilidadDeResumen.Seleccionar(filas, huella);
    }
}
