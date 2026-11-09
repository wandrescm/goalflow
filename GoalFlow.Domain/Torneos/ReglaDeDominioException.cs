namespace GoalFlow.Domain.Torneos;

/// <summary>Razones estables de una regla de dominio rota (Spec 005 R-12).</summary>
public static class RazonesDeDominio
{
    public const string CodigoInvalido = "codigo_invalido";
    public const string NombreInvalido = "nombre_invalido";
    public const string JornadaInvalida = "jornada_invalida";
    public const string VueltasInvalidas = "vueltas_invalidas";
    public const string OrdenInvalido = "orden_invalido";
    public const string LocalIgualAVisitante = "local_igual_a_visitante";
    public const string PartidoConResultado = "partido_con_resultado";
}

/// <summary>Se rompió una regla de dominio de R-12. <see cref="Razon"/> es un código estable, sin datos del usuario.</summary>
public class ReglaDeDominioException(string razon) : Exception($"Regla de dominio rota: {razon}.")
{
    public string Razon { get; } = razon;
}
