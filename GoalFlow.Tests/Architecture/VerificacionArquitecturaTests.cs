namespace GoalFlow.Tests.Architecture;

/// <summary>Spec 008 T-14 (R-21): el verificador es puro, y la regla que lo vigila ve también el IL de los cuerpos.</summary>
public class VerificacionArquitecturaTests
{
    [Fact]
    public void T14_los_tipos_de_Verificacion_no_referencian_nada_prohibido_ni_en_firmas_ni_en_cuerpos()
    {
        var tipos = ReglasDeConsultas.TiposEnNamespace(GoalFlow.Application.AssemblyReference.Assembly, ReglasDeVerificacion.EspacioVerificado);

        // Anti-vacío: el namespace tiene tipos, y entre ellos el verificador.
        Assert.NotEmpty(tipos);
        Assert.Contains(tipos, t => t.Name == "VerificadorDeResumen");

        var resultado = ReglasDeVerificacion.Analizar(tipos);

        Assert.True(resultado.Cumple, resultado.ToString());
    }

    [Fact]
    public void T14_canario_un_Regex_solo_dentro_de_un_cuerpo_hace_fallar_la_regla()
    {
        var tipos = ReglasDeConsultas.TiposEnNamespace(
            typeof(VerificacionArquitecturaTests).Assembly, "GoalFlow.Tests.Architecture.Fixtures.Verificacion");
        Assert.NotEmpty(tipos);

        var resultado = ReglasDeVerificacion.Analizar(tipos);

        Assert.False(resultado.Cumple, "El canario debía hacer fallar la regla");
        Assert.Contains(resultado.Violaciones, v => v.Contains("cuerpo", StringComparison.Ordinal)
            && v.Contains("System.Text.RegularExpressions.Regex", StringComparison.Ordinal));

        // Las firmas del canario son limpias: la regla lo encontró por el IL del método, no por una firma.
        Assert.DoesNotContain(resultado.Violaciones, v => v.Contains("System.Text.RegularExpressions", StringComparison.Ordinal)
            && v.Contains("firma", StringComparison.Ordinal));
    }
}
