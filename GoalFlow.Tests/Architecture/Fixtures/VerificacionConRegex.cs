// Fixture del canario de la Spec 008 (T-14): un tipo con firmas limpias que crea un Regex solo dentro del cuerpo de un método.
// Vive en su propio namespace: ninguna otra prueba recorre este namespace.
namespace GoalFlow.Tests.Architecture.Fixtures.Verificacion
{
    public static class CuerpoConRegex
    {
        public static bool Coincide(string texto) => new System.Text.RegularExpressions.Regex("a+").IsMatch(texto);
    }
}
