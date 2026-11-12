using System.Text.Json;

namespace GoalFlow.Application.IA;

/// <summary>El resultado de revisar el texto de la respuesta. <see cref="Texto"/> es el texto recortado, solo si es válido.</summary>
public sealed record ResultadoDeSalida(bool EsValida, string? Texto);

/// <summary>
/// La salida válida (Spec 007 R-24): un objeto JSON cuyo único miembro es <c>texto</c>, una cadena que, recortada, tiene de 80 a
/// 900 caracteres y ningún carácter de control. Es la revisión de forma; el verificador de nombres y números es de H15.
/// </summary>
public static class ValidadorDeSalidaDelModelo
{
    public const int LongitudMinima = 80;
    public const int LongitudMaxima = 900;

    public static ResultadoDeSalida Validar(string textoDeLaRespuesta)
    {
        var invalida = new ResultadoDeSalida(false, null);
        try
        {
            using var documento = JsonDocument.Parse(textoDeLaRespuesta);
            var raiz = documento.RootElement;
            if (raiz.ValueKind != JsonValueKind.Object) return invalida;

            var miembros = raiz.EnumerateObject().ToList();
            if (miembros.Count != 1 || miembros[0].Name != "texto" || miembros[0].Value.ValueKind != JsonValueKind.String)
                return invalida;

            var texto = miembros[0].Value.GetString()!.Trim();
            if (texto.Length is < LongitudMinima or > LongitudMaxima) return invalida;
            if (texto.Any(char.IsControl)) return invalida;

            return new ResultadoDeSalida(true, texto);
        }
        catch (JsonException)
        {
            return invalida;
        }
    }
}
