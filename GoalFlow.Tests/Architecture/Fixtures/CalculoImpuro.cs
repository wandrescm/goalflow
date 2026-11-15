// Canario de la Spec 009 (T-18, R-32): tipos con firmas limpias cuyos cuerpos usan el reloj, la cultura y E/S.
// Viven en su propio namespace: ninguna otra prueba recorre este namespace.
namespace GoalFlow.Tests.Architecture.Fixtures.CalculoImpuro
{
    public static class CuerpoConReloj
    {
        public static int Anio() => System.DateTime.UtcNow.Year;
    }

    public static class CuerpoConCultura
    {
        public static string Formatear(int numero) => numero.ToString(System.Globalization.CultureInfo.CurrentCulture);
    }

    public static class CuerpoConEntradaYSalida
    {
        public static bool Existe(string ruta) => System.IO.File.Exists(ruta);
    }

    public static class CuerpoConRegex
    {
        public static bool Coincide(string texto) => new System.Text.RegularExpressions.Regex("a+").IsMatch(texto);
    }
}
