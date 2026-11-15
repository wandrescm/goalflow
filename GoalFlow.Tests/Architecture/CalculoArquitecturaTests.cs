namespace GoalFlow.Tests.Architecture;

/// <summary>
/// Spec 009 T-18 (R-30 a R-32): el cálculo de Estadísticas es puro, y la regla que lo vigila ve también el IL de los cuerpos.
/// El grafo de módulos y el conjunto exacto de módulos de Application están en <c>MultiTenancyArquitecturaTests</c>.
/// </summary>
public class CalculoArquitecturaTests
{
    [Fact]
    public void T18_los_tipos_de_Calculo_no_referencian_nada_prohibido_ni_en_firmas_ni_en_cuerpos()
    {
        var tipos = ReglasDeConsultas.TiposEnNamespace(GoalFlow.Application.AssemblyReference.Assembly, ReglasDeCalculo.EspacioVerificado);

        // Anti-vacío: la selección contiene las tres calculadoras.
        Assert.Contains(tipos, t => t.Name == "CalculadoraDePosiciones");
        Assert.Contains(tipos, t => t.Name == "CalculadoraDeGoleadores");
        Assert.Contains(tipos, t => t.Name == "CalculadoraDeValla");

        var resultado = ReglasDeCalculo.Analizar(tipos);

        Assert.True(resultado.Cumple, resultado.ToString());
    }

    [Fact]
    public void T18_la_seleccion_son_exactamente_los_tipos_del_namespace_de_Calculo()
    {
        var evaluados = ReglasDeConsultas.TiposEnNamespace(GoalFlow.Application.AssemblyReference.Assembly, ReglasDeCalculo.EspacioVerificado);

        static Type Externo(Type tipo)
        {
            while (tipo.DeclaringType is not null) tipo = tipo.DeclaringType;
            return tipo;
        }

        // Todo tipo del ensamblado (incluidos los anidados) cuyo tipo externo vive en ese namespace, y solo esos.
        var esperados = GoalFlow.Application.AssemblyReference.Assembly.GetTypes()
            .Where(t => Externo(t).Namespace == ReglasDeCalculo.EspacioVerificado)
            .Select(t => t.FullName!.Replace('+', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(esperados);
        Assert.Equal(esperados, evaluados.Select(t => t.FullName).Order(StringComparer.Ordinal).ToList());
        // La selección alcanza los tipos de resultado, no solo las calculadoras.
        Assert.Contains(evaluados, t => t.Name == "ResultadoDePosiciones");
    }

    [Fact]
    public void T18_canario_el_reloj_la_cultura_la_entrada_y_salida_y_Regex_dentro_de_cuerpos_hacen_fallar_la_regla()
    {
        var tipos = ReglasDeConsultas.TiposEnNamespace(
            typeof(CalculoArquitecturaTests).Assembly, "GoalFlow.Tests.Architecture.Fixtures.CalculoImpuro");
        Assert.NotEmpty(tipos);

        var resultado = ReglasDeCalculo.Analizar(tipos);

        Assert.False(resultado.Cumple, "El canario debía hacer fallar la regla");
        foreach (var referido in new[] { "System.DateTime", "System.Globalization.CultureInfo", "System.IO.File", "System.Text.RegularExpressions.Regex" })
            Assert.Contains(resultado.Violaciones, v => v.Contains("cuerpo", StringComparison.Ordinal) && v.Contains(referido, StringComparison.Ordinal));

        // Las firmas del canario son limpias: la regla lo encontró por el IL de los métodos.
        Assert.DoesNotContain(resultado.Violaciones, v => v.Contains("firma", StringComparison.Ordinal));
    }
}
