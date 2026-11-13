using System.Globalization;
using System.Text;
using GoalFlow.Application.Torneos.Contracts;

namespace GoalFlow.Application.IA.Verificacion;

/// <summary>
/// El verificador determinista de A-01 (Spec 008 R-01 a R-11, ADR-12 §2.6): una función pura del texto y de la entrada canónica
/// que lo produjo. Sin E/S, sin expresiones regulares, sin reloj ni cultura (R-21): un pase de enmascarado y un pase lineal por
/// regla. Cualquier cambio de las reglas o de las listas de palabras sube <see cref="VersionDelVerificador"/> y vuelve a correr el corpus.
/// </summary>
public static class VerificadorDeResumen
{
    /// <summary>La versión de las reglas R-02 a R-11. Va en el código de resultado de una llamada rechazada.</summary>
    public const string VersionDelVerificador = "v1";

    public const string V1 = "V1";
    public const string V2 = "V2";
    public const string V3 = "V3";
    public const string V4 = "V4";
    public const string V5 = "V5";
    public const string V6 = "V6";
    public const string V7 = "V7";

    // El marcador de un valor enmascarado: un carácter de uso privado, que no es letra, dígito ni terminador (R-02).
    private const char Marcador = '';

    // R-05
    private static readonly HashSet<string> Inicios = new(StringComparer.Ordinal)
    {
        "en", "el", "la", "los", "las", "un", "una", "por", "para", "con", "sin", "no", "ni", "se", "ademas", "tambien",
        "ambos", "ambas", "tras", "hubo", "fue", "goles", "tarjetas", "detalle",
    };

    // R-06
    private static readonly Dictionary<string, int> Numeros = new(StringComparer.Ordinal)
    {
        ["cero"] = 0, ["uno"] = 1, ["dos"] = 2, ["tres"] = 3, ["cuatro"] = 4, ["cinco"] = 5, ["seis"] = 6, ["siete"] = 7,
        ["ocho"] = 8, ["nueve"] = 9, ["diez"] = 10, ["once"] = 11, ["doce"] = 12, ["trece"] = 13, ["catorce"] = 14,
        ["quince"] = 15, ["dieciseis"] = 16, ["diecisiete"] = 17, ["dieciocho"] = 18, ["diecinueve"] = 19, ["veinte"] = 20,
        ["doblete"] = 2, ["triplete"] = 3,
    };

    // R-07
    private static readonly string[] Cronologia =
    [
        "minut*", "tiempo", "tiempos", "mitad*", "descanso*", "entretiempo*", "inicio", "comienzo", "finalmente", "al final",
        "sobre el final", "abrio", "cerro", "adelant*", "remont*", "descont*", "respondi*", "ampli*", "ventaja*", "sentenci*",
        "luego", "despues", "posterior*", "antes", "enseguida", "tempran*", "agonic*", "primer gol", "segundo gol", "ultimo gol",
    ];

    // R-08
    private static readonly string[] Incidencias =
    [
        "arbitr*", "juez", "jueces", "incidenc*", "polemic*", "protest*", "expuls*", "penal*", "penalty", "corner*",
        "tiro libre", "fuera de juego", "lesion*", "asist*",
    ];

    // R-09
    private static readonly string[] Ambiente = ["clima", "lluvi*", "cancha*", "estadio*", "hinch*", "aficion*", "publico"];

    // R-10
    private static readonly string[] Tarjeta = ["tarjeta*"];
    private static readonly string[] Informacion = ["informad*", "informaron", "informo", "informacion", "reportad*"];
    private static readonly string[] Negacion = ["no", "ni", "sin", "ninguna", "ningun", "ninguno"];

    // R-11
    private static readonly string[] Amarillo = ["amarill*"];
    private static readonly string[] Rojo = ["roj*"];
    private static readonly string[] Azul = ["azul*"];

    private enum TipoDeToken
    {
        Letras,
        Digitos,
        Enmascarado,
    }

    private readonly record struct Token(TipoDeToken Tipo, string Texto, string Plegado);

    /// <summary>Verifica <paramref name="texto"/> contra la entrada canónica que lo produjo. Devuelve las reglas que fallaron, en orden.</summary>
    public static ResultadoDeVerificacion Verificar(string texto, EntradaCanonica entrada)
    {
        ArgumentNullException.ThrowIfNull(texto);
        ArgumentNullException.ThrowIfNull(entrada);

        var oraciones = Segmentar(Enmascarar(texto, entrada));
        var numerosPermitidos = NumerosDeLaEntrada(entrada);

        var fallas = new List<string>(7);
        if (FallaElNombre(oraciones)) fallas.Add(V1);
        if (FallaUnNumero(oraciones, numerosPermitidos)) fallas.Add(V2);
        if (CoincideConLista(oraciones, Cronologia)) fallas.Add(V3);
        if (CoincideConLista(oraciones, Incidencias)) fallas.Add(V4);
        if (CoincideConLista(oraciones, Ambiente)) fallas.Add(V5);
        if (FallaLaInformacionDeTarjetas(oraciones, entrada)) fallas.Add(V6);
        if (FallaUnColor(oraciones, entrada)) fallas.Add(V7);
        return new ResultadoDeVerificacion(fallas);
    }

    // ---- R-02: valores enmascarados ----

    private static string Enmascarar(string texto, EntradaCanonica entrada)
    {
        var valores = new HashSet<string>(StringComparer.Ordinal)
        {
            entrada.Torneo.ToLowerInvariant(),
            entrada.Local.ToLowerInvariant(),
            entrada.Visitante.ToLowerInvariant(),
            entrada.Estado.ToLowerInvariant(),
            "w.o.",
        };
        foreach (var g in entrada.Goles) valores.Add(g.Jugador.ToLowerInvariant());
        foreach (var a in entrada.Autogoles) valores.Add(a.Jugador.ToLowerInvariant());
        foreach (var t in entrada.Tarjetas) valores.Add(t.Jugador.ToLowerInvariant());
        valores.Remove(string.Empty);

        // De la más larga a la más corta; a igual longitud, en orden ordinal.
        var ordenados = valores.ToList();
        ordenados.Sort(static (x, y) => x.Length != y.Length ? y.Length.CompareTo(x.Length) : string.CompareOrdinal(x, y));

        // Un carácter del propio texto que se parezca al marcador no puede hacerse pasar por un valor enmascarado.
        var original = texto.Replace(Marcador, ' ');
        var minuscula = original.ToLowerInvariant();

        var salida = new StringBuilder(original.Length);
        var i = 0;
        while (i < original.Length)
        {
            var longitud = LongitudDelValorEn(minuscula, i, ordenados);
            if (longitud > 0)
            {
                salida.Append(Marcador);
                i += longitud;
            }
            else
            {
                salida.Append(original[i]);
                i++;
            }
        }

        return salida.ToString();
    }

    private static int LongitudDelValorEn(string minuscula, int posicion, List<string> valores)
    {
        if (posicion > 0 && char.IsLetterOrDigit(minuscula[posicion - 1])) return 0;
        foreach (var valor in valores)
        {
            var fin = posicion + valor.Length;
            if (fin > minuscula.Length) continue;
            if (string.CompareOrdinal(minuscula, posicion, valor, 0, valor.Length) != 0) continue;
            if (fin < minuscula.Length && char.IsLetterOrDigit(minuscula[fin])) continue;
            return valor.Length;
        }

        return 0;
    }

    // ---- R-03: oraciones y tokens ----

    private static List<List<Token>> Segmentar(string enmascarado)
    {
        var oraciones = new List<List<Token>>();
        var actual = new List<Token>();
        var i = 0;
        while (i < enmascarado.Length)
        {
            var c = enmascarado[i];
            if (c is '.' or '!' or '?' or ';')
            {
                oraciones.Add(actual);
                actual = [];
                i++;
            }
            else if (c == Marcador)
            {
                actual.Add(new Token(TipoDeToken.Enmascarado, Marcador.ToString(), Marcador.ToString()));
                i++;
            }
            else if (char.IsLetter(c))
            {
                var inicio = i;
                while (i < enmascarado.Length && char.IsLetter(enmascarado[i])) i++;
                var palabra = enmascarado[inicio..i];
                actual.Add(new Token(TipoDeToken.Letras, palabra, Plegar(palabra)));
            }
            else if (char.IsDigit(c))
            {
                var inicio = i;
                while (i < enmascarado.Length && char.IsDigit(enmascarado[i])) i++;
                var numero = enmascarado[inicio..i];
                actual.Add(new Token(TipoDeToken.Digitos, numero, numero));
            }
            else
            {
                i++;
            }
        }

        oraciones.Add(actual);
        return oraciones;
    }

    // La forma plegada: minúscula invariante y sin marcas no espaciadoras tras la descomposición NFD ("además" → "ademas").
    private static string Plegar(string palabra)
    {
        var descompuesta = palabra.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var salida = new StringBuilder(descompuesta.Length);
        foreach (var c in descompuesta)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) salida.Append(c);
        }

        return salida.ToString();
    }

    private static bool CoincideElemento(string elemento, string plegado) =>
        elemento.EndsWith('*')
            ? plegado.StartsWith(elemento.AsSpan(0, elemento.Length - 1), StringComparison.Ordinal)
            : string.Equals(elemento, plegado, StringComparison.Ordinal);

    private static bool TieneToken(List<Token> oracion, string[] lista)
    {
        foreach (var token in oracion)
        {
            if (token.Tipo != TipoDeToken.Letras) continue;
            foreach (var elemento in lista)
            {
                if (CoincideElemento(elemento, token.Plegado)) return true;
            }
        }

        return false;
    }

    // ---- R-04: el conjunto A ----

    private static HashSet<int> NumerosDeLaEntrada(EntradaCanonica e)
    {
        var a = new HashSet<int> { e.Fase, e.Jornada, e.GolesSinAutor.Local, e.GolesSinAutor.Visitante };
        if (e.Marcador is not null)
        {
            a.Add(e.Marcador.Local);
            a.Add(e.Marcador.Visitante);
            a.Add(e.Marcador.Local + e.Marcador.Visitante);
        }

        if (e.Oficial is not null)
        {
            a.Add(e.Oficial.Local);
            a.Add(e.Oficial.Visitante);
        }

        foreach (var g in e.Goles) a.Add(g.Cantidad);
        foreach (var x in e.Autogoles) a.Add(x.Cantidad);
        var sumaDeTarjetas = 0;
        foreach (var t in e.Tarjetas)
        {
            a.Add(t.Cantidad);
            sumaDeTarjetas += t.Cantidad;
        }

        a.Add(sumaDeTarjetas);
        return a;
    }

    // ---- R-05: V1, nombre ----

    private static bool FallaElNombre(List<List<Token>> oraciones)
    {
        foreach (var oracion in oraciones)
        {
            for (var k = 0; k < oracion.Count; k++)
            {
                var token = oracion[k];
                if (token.Tipo != TipoDeToken.Letras || !char.IsUpper(token.Texto[0])) continue;
                if (k > 0 || !Inicios.Contains(token.Plegado)) return true;
            }
        }

        return false;
    }

    // ---- R-06: V2, número ----

    private static bool FallaUnNumero(List<List<Token>> oraciones, HashSet<int> permitidos)
    {
        foreach (var oracion in oraciones)
        {
            foreach (var token in oracion)
            {
                if (token.Tipo == TipoDeToken.Digitos)
                {
                    if (token.Texto.Length > 6) return true;
                    var valor = 0;
                    foreach (var d in token.Texto) valor = (valor * 10) + CharUnicodeInfo.GetDecimalDigitValue(d);
                    if (!permitidos.Contains(valor)) return true;
                }
                else if (token.Tipo == TipoDeToken.Letras && Numeros.TryGetValue(token.Plegado, out var numero) && !permitidos.Contains(numero))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // ---- R-07 a R-09: V3, V4 y V5 ----

    private static bool CoincideConLista(List<List<Token>> oraciones, string[] lista)
    {
        foreach (var oracion in oraciones)
        {
            foreach (var elemento in lista)
            {
                var partes = elemento.Split(' ');
                for (var inicio = 0; inicio + partes.Length <= oracion.Count; inicio++)
                {
                    if (CoincideEn(oracion, inicio, partes)) return true;
                }
            }
        }

        return false;
    }

    private static bool CoincideEn(List<Token> oracion, int inicio, string[] partes)
    {
        for (var p = 0; p < partes.Length; p++)
        {
            var token = oracion[inicio + p];
            if (token.Tipo != TipoDeToken.Letras || !CoincideElemento(partes[p], token.Plegado)) return false;
        }

        return true;
    }

    // ---- R-10: V6, informe de tarjetas ----

    private static bool FallaLaInformacionDeTarjetas(List<List<Token>> oraciones, EntradaCanonica e)
    {
        foreach (var oracion in oraciones)
        {
            if (!TieneToken(oracion, Tarjeta)) continue;
            var informa = TieneToken(oracion, Informacion);

            if (e.TarjetasInformadas != true)
            {
                // false o null: se puede decir que no se informaron, nunca que no hubo.
                if (!informa) return true;
            }
            else if (e.Tarjetas.Count == 0)
            {
                // true sin tarjetas: "no hubo tarjetas informadas" se lee también como "no se informaron".
                if (informa) return true;
            }
            else if (TieneToken(oracion, Negacion) && !oracion.Exists(static t => t.Tipo == TipoDeToken.Enmascarado))
            {
                // true con tarjetas: una negación sin nombre niega tarjetas que sí se mostraron.
                return true;
            }
        }

        return false;
    }

    // ---- R-11: V7, colores ----

    private static bool FallaUnColor(List<List<Token>> oraciones, EntradaCanonica e)
    {
        var hayAmarilla = e.Tarjetas.Any(static t => t.Color == CodigosDeTorneos.Amarilla);
        var hayRoja = e.Tarjetas.Any(static t => t.Color == CodigosDeTorneos.Roja);
        var hayAzul = e.Tarjetas.Any(static t => t.Color == CodigosDeTorneos.Azul);
        foreach (var oracion in oraciones)
        {
            if (!hayAmarilla && TieneToken(oracion, Amarillo)) return true;
            if (!hayRoja && TieneToken(oracion, Rojo)) return true;
            if (!hayAzul && TieneToken(oracion, Azul)) return true;
        }

        return false;
    }
}
