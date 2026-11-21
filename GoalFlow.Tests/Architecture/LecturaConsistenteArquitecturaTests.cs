namespace GoalFlow.Tests.Architecture;

/// <summary>Spec 011 R-20 y C-12 (T-28 provisional): los tres casos de uso públicos abren una sola lectura consistente y nunca <c>IniciarAsync</c>.</summary>
public class LecturaConsistenteArquitecturaTests
{
    private const string EspacioDelCanarioA = "GoalFlow.Tests.Architecture.Fixtures.LecturaConsistente.ConIniciarAsync";
    private const string EspacioDelCanarioB = "GoalFlow.Tests.Architecture.Fixtures.LecturaConsistente.SinLectura";

    private static IReadOnlyList<Mono.Cecil.TypeDefinition> Canario(string espacio) =>
        ReglasDeConsultas.TiposEnNamespace(typeof(LecturaConsistenteArquitecturaTests).Assembly, espacio);

    [Fact]
    public void T28_los_tres_casos_de_uso_publicos_abren_la_lectura_consistente_y_nunca_IniciarAsync()
    {
        var evaluados = ReglasDeLecturaConsistente.TiposEvaluados(
            ReglasDeConsultas.CargarTipos(GoalFlow.Application.AssemblyReference.Assembly), ReglasDeLecturaConsistente.CasosDeUsoPublicos);

        // La selección se afirma: exactamente los tres tipos externos, en orden ordinal.
        Assert.Equal(
            ReglasDeLecturaConsistente.CasosDeUsoPublicos.Order(StringComparer.Ordinal).ToArray(),
            evaluados.Select(t => ReglasDeConsultas.TipoExterno(t).FullName).Distinct().Order(StringComparer.Ordinal).ToArray());
        // La regla ve las máquinas de estado: hay al menos un tipo anidado o generado.
        Assert.Contains(evaluados, t => t.DeclaringType is not null);

        var resultado = ReglasDeLecturaConsistente.Analizar(evaluados, ReglasDeLecturaConsistente.CasosDeUsoPublicos);

        Assert.True(resultado.Cumple, resultado.ToString());
    }

    [Fact]
    public void T28_canario_A_un_tipo_que_llama_tambien_a_IniciarAsync_hace_fallar_la_regla_aunque_abra_la_lectura_consistente()
    {
        var tipos = Canario(EspacioDelCanarioA);
        Assert.NotEmpty(tipos);
        var exigidos = new[] { $"{EspacioDelCanarioA}.CasoConEscritura" };

        var resultado = ReglasDeLecturaConsistente.Analizar(ReglasDeLecturaConsistente.TiposEvaluados(tipos, exigidos), exigidos);

        Assert.False(resultado.Cumple, "El canario A debía hacer fallar la regla");
        Assert.Contains(resultado.Violaciones, v => v.Contains("IniciarAsync", StringComparison.Ordinal) && v.Contains("CasoConEscritura", StringComparison.Ordinal));
        // (i) se cumple: solo falla (ii).
        Assert.DoesNotContain(resultado.Violaciones, v => v.Contains("falta la llamada", StringComparison.Ordinal));
    }

    [Fact]
    public void T28_canario_B_un_tipo_sin_lectura_consistente_que_llama_a_IniciarAsync_falla_por_las_dos_condiciones()
    {
        var tipos = Canario(EspacioDelCanarioB);
        Assert.NotEmpty(tipos);
        var exigidos = new[] { $"{EspacioDelCanarioB}.CasoSinLectura" };

        var resultado = ReglasDeLecturaConsistente.Analizar(ReglasDeLecturaConsistente.TiposEvaluados(tipos, exigidos), exigidos);

        Assert.False(resultado.Cumple, "El canario B debía hacer fallar la regla");
        Assert.Contains(resultado.Violaciones, v => v.Contains("falta la llamada", StringComparison.Ordinal) && v.Contains("IniciarLecturaConsistenteAsync", StringComparison.Ordinal));
        Assert.Contains(resultado.Violaciones, v => v.Contains("llama a", StringComparison.Ordinal) && v.Contains("IniciarAsync", StringComparison.Ordinal));
    }
}
