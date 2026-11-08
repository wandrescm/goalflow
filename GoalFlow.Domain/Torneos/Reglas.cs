using System.Globalization;
using System.Text.RegularExpressions;

namespace GoalFlow.Domain.Torneos;

/// <summary>Reglas de formato de R-12, compartidas por las entidades y por el validador del fixture.</summary>
internal static partial class Reglas
{
    public const int LongitudMaximaDeNombre = 80;

    [GeneratedRegex(@"^[A-Za-z0-9_-]{1,32}\z", RegexOptions.CultureInvariant)]
    private static partial Regex PatronDeCodigo();

    public static bool CodigoValido(string? codigo) => codigo is not null && PatronDeCodigo().IsMatch(codigo);

    /// <summary>El nombre recortado, o <c>null</c> si está vacío, pasa de 80 caracteres o tiene caracteres de control.</summary>
    public static string? NombreNormalizado(string? nombre)
    {
        var recortado = nombre?.Trim();
        if (string.IsNullOrEmpty(recortado) || recortado.Length > LongitudMaximaDeNombre) return null;
        return recortado.Any(char.IsControl) ? null : recortado;
    }

    public static string ExigirCodigo(string? codigo) =>
        CodigoValido(codigo) ? codigo! : throw new ReglaDeDominioException(RazonesDeDominio.CodigoInvalido);

    public static string ExigirNombre(string? nombre) =>
        NombreNormalizado(nombre) ?? throw new ReglaDeDominioException(RazonesDeDominio.NombreInvalido);

    public static int ExigirJornada(int jornada) =>
        jornada >= 1 ? jornada : throw new ReglaDeDominioException(RazonesDeDominio.JornadaInvalida);

    public static int ExigirVueltas(int vueltas) =>
        vueltas is 1 or 2 ? vueltas : throw new ReglaDeDominioException(RazonesDeDominio.VueltasInvalidas);

    public static int ExigirOrden(int orden) =>
        orden >= 1 ? orden : throw new ReglaDeDominioException(RazonesDeDominio.OrdenInvalido);

    /// <summary>UTC con precisión de microsegundos, la de <c>timestamptz</c>: así una hora igual se reconoce como igual (R-20).</summary>
    public static DateTimeOffset? HoraEnUtc(DateTimeOffset? hora) =>
        hora is { } h ? new DateTimeOffset(h.UtcDateTime.AddTicks(-(h.UtcDateTime.Ticks % 10)), TimeSpan.Zero) : null;
}

/// <summary>Interpreta la hora programada del fixture: solo se acepta con <c>Z</c> o con un desplazamiento explícito (R-17).</summary>
public static partial class HoraProgramada
{
    public const string SinDesplazamiento = "hora_sin_desplazamiento";
    public const string Invalida = "hora_invalida";

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d{1,7})?)?(?<desplazamiento>Z|[+-]\d{2}:\d{2})?\z", RegexOptions.CultureInvariant)]
    private static partial Regex Patron();

    /// <returns>El código del error, o <c>null</c> si la hora es válida (y entonces <paramref name="valor"/> viene en UTC).</returns>
    public static string? Interpretar(string texto, out DateTimeOffset valor)
    {
        valor = default;
        var coincidencia = Patron().Match(texto);
        if (!coincidencia.Success) return Invalida;
        if (!coincidencia.Groups["desplazamiento"].Success) return SinDesplazamiento;

        if (!DateTimeOffset.TryParse(texto, CultureInfo.InvariantCulture, DateTimeStyles.None, out var interpretado))
            return Invalida;

        valor = Reglas.HoraEnUtc(interpretado)!.Value;
        return null;
    }
}

/// <summary>Formato de los códigos de R-12, para quien valida antes de construir entidades (por ejemplo la ruta de la API).</summary>
public static class FormatoDeCodigos
{
    public static bool EsValido(string? codigo) => Reglas.CodigoValido(codigo);
}
