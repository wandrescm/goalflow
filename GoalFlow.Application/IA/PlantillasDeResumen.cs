using GoalFlow.Application.Torneos.Contracts;

namespace GoalFlow.Application.IA;

/// <summary>
/// Una planilla que no pudo pasar la validación de Torneos llegó hasta aquí: una plantilla nunca interpola un valor ausente
/// (Spec 007 R-11, ADR-12 C-16). El consumidor la convierte en el fallo permanente <see cref="CodigoDeRazon"/>.
/// </summary>
public class PlanillaIncoherenteException() : Exception("La planilla no tiene el marcador que su estado exige.")
{
    public const string CodigoDeRazon = "planilla_incoherente";
}

/// <summary>Plantillas y borradores deterministas: funciones puras de la entrada canónica (Spec 007 R-11). Los nombres y números salen solo de ella.</summary>
public static class PlantillasDeResumen
{
    public const string TextoDelBorradorCompacto = " Detalle de goles y tarjetas omitido por extensión.";

    /// <summary>La plantilla de un <c>Jugado</c> sin eventos con nombre, o de un <c>Adjudicado</c>.</summary>
    /// <exception cref="PlanillaIncoherenteException">Falta el marcador que el estado exige.</exception>
    public static string Plantilla(EntradaCanonica e)
    {
        switch (e.Estado)
        {
            case CodigosDeTorneos.Jugado:
                var texto = LineaDeResultado(e);
                var sinAutor = e.GolesSinAutor;
                if (sinAutor.Local + sinAutor.Visitante > 0)
                    texto += $" Goles sin autor registrado: {e.Local} {sinAutor.Local}, {e.Visitante} {sinAutor.Visitante}.";
                if (e.TarjetasInformadas == false)
                    texto += " Tarjetas no informadas.";
                return texto;

            case CodigosDeTorneos.Adjudicado:
                var oficial = e.Oficial ?? throw new PlanillaIncoherenteException();
                var motivo = e.Motivo switch
                {
                    CodigosDeTorneos.WalkOver => "W.O.",
                    CodigosDeTorneos.Sancion => "sanción",
                    CodigosDeTorneos.Otro => "decisión administrativa",
                    _ => throw new PlanillaIncoherenteException(),
                };
                return $"{e.Local} {oficial.Local}-{oficial.Visitante} {e.Visitante}, resultado oficial por {motivo}.";

            default:
                throw new ArgumentException("Solo Jugado y Adjudicado tienen plantilla.", nameof(e));
        }
    }

    /// <summary>
    /// El borrador extendido (casos narrativos): resultado, goles por lado y tarjetas. Si pasara de
    /// <paramref name="longitudMaxima"/>, el compacto: el resultado y una frase fija (nada se corta a la mitad).
    /// </summary>
    /// <exception cref="PlanillaIncoherenteException">Falta el marcador que el estado exige.</exception>
    public static string BorradorExtendido(EntradaCanonica e, int longitudMaxima)
    {
        var linea = LineaDeResultado(e);
        var texto = linea;

        foreach (var lado in new[] { LadoDelEquipo.Local, LadoDelEquipo.Visitante })
        {
            var items = new List<string>();
            items.AddRange(e.Goles.Where(g => g.Equipo == lado).Select(g => Con(g.Jugador, g.Cantidad)));
            items.AddRange(e.Autogoles.Where(a => EntradaCanonica.Contrario(a.EquipoDelAutor) == lado)
                .Select(a => Con($"autogol de {a.Jugador} ({e.NombreDelEquipo(a.EquipoDelAutor)})", a.Cantidad)));
            var sinAutor = lado == LadoDelEquipo.Local ? e.GolesSinAutor.Local : e.GolesSinAutor.Visitante;
            if (sinAutor > 0) items.Add($"{sinAutor} sin autor");

            if (items.Count > 0) texto += $" Goles de {e.NombreDelEquipo(lado)}: {string.Join(", ", items)}.";
        }

        if (e.Tarjetas.Count > 0)
        {
            var tarjetas = e.Tarjetas.Select(t =>
                $"{t.Jugador} ({e.NombreDelEquipo(t.Equipo)}) {Color(t.Color)}{(t.Cantidad > 1 ? $" x{t.Cantidad}" : string.Empty)}");
            texto += $" Tarjetas: {string.Join(", ", tarjetas)}.";
        }
        else
        {
            texto += e.TarjetasInformadas == true ? " Sin tarjetas." : " Tarjetas no informadas.";
        }

        return texto.Length > longitudMaxima ? linea + TextoDelBorradorCompacto : texto;
    }

    // El marcador oficial para un Adjudicado y el deportivo en los demás casos; nunca un valor inventado.
    private static string LineaDeResultado(EntradaCanonica e)
    {
        var marcador = e.Estado == CodigosDeTorneos.Adjudicado ? e.Oficial : e.Marcador;
        if (marcador is null) throw new PlanillaIncoherenteException();
        return $"{e.Local} {marcador.Local}-{marcador.Visitante} {e.Visitante}.";
    }

    private static string Con(string nombre, int cantidad) => cantidad > 1 ? $"{nombre} ({cantidad})" : nombre;

    private static string Color(string color) => color switch
    {
        CodigosDeTorneos.Amarilla => "amarilla",
        CodigosDeTorneos.Azul => "azul",
        CodigosDeTorneos.Roja => "roja",
        _ => throw new ArgumentOutOfRangeException(nameof(color), "Color de tarjeta desconocido."),
    };
}
