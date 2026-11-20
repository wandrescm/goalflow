using Mono.Cecil;

namespace GoalFlow.Tests.Architecture;

/// <summary>
/// Spec 011 R-20 / C-12 (T-28 provisional): cada caso de uso de lectura pública abre <b>una</b> transacción de lectura consistente
/// (<c>REPEATABLE READ</c>). Para cada tipo externo exigido, y con sus tipos anidados y generados por el compilador (las llamadas viven en
/// el <c>MoveNext</c> de los métodos <c>async</c>): (i) algún cuerpo llama a <c>IUnidadDeTrabajo.IniciarLecturaConsistenteAsync</c> y
/// (ii) ningún cuerpo llama a <c>IUnidadDeTrabajo.IniciarAsync</c>. Se compara por tipo declarante y nombre del método. Las pruebas
/// reales y los canarios invocan exactamente <see cref="Analizar"/>.
/// </summary>
public static class ReglasDeLecturaConsistente
{
    public const string UnidadDeTrabajo = "GoalFlow.Application.Persistencia.IUnidadDeTrabajo";
    public const string LecturaConsistente = "IniciarLecturaConsistenteAsync";
    public const string Escritura = "IniciarAsync";

    public static readonly IReadOnlyList<string> CasosDeUsoPublicos =
    [
        "GoalFlow.Application.IA.ConsultaDeDetalleDePartido",
        "GoalFlow.Application.Torneos.ConsultaDePartidosDeFase",
        "GoalFlow.Application.Torneos.ConsultaDeTorneosDeLaLiga",
    ];

    /// <summary>Los tipos cuyo tipo externo es exactamente uno de <paramref name="exigidos"/>, anidados y generados incluidos.</summary>
    public static IReadOnlyList<TypeDefinition> TiposEvaluados(IEnumerable<TypeDefinition> todos, IReadOnlyCollection<string> exigidos) =>
        [.. todos.Where(t => exigidos.Contains(ReglasDeConsultas.TipoExterno(t).FullName))];

    /// <summary>Cada violación nombra el tipo externo y el miembro (o dice que falta la llamada exigida).</summary>
    public static ResultadoRegla Analizar(IEnumerable<TypeDefinition> tipos, IReadOnlyCollection<string> exigidos)
    {
        var porTipo = tipos.GroupBy(t => ReglasDeConsultas.TipoExterno(t).FullName).ToDictionary(g => g.Key, g => g.ToList());
        var violaciones = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var externo in exigidos)
        {
            var llamadas = porTipo.GetValueOrDefault(externo, []).SelectMany(Llamadas).ToList();
            if (!llamadas.Any(m => EsUnidadDeTrabajo(m, LecturaConsistente)))
                violaciones.Add($"{externo} · falta la llamada a {UnidadDeTrabajo}::{LecturaConsistente}");
            foreach (var m in llamadas.Where(m => EsUnidadDeTrabajo(m, Escritura)))
                violaciones.Add($"{externo} · llama a {m.DeclaringType.FullName}::{m.Name}");
        }

        return ResultadoRegla.Desde([.. violaciones]);
    }

    private static bool EsUnidadDeTrabajo(MethodReference metodo, string nombre) =>
        metodo.Name == nombre && metodo.DeclaringType.FullName == UnidadDeTrabajo;

    private static IEnumerable<MethodReference> Llamadas(TypeDefinition tipo) =>
        tipo.Methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions)
            .Select(i => i.Operand).OfType<MethodReference>();
}
