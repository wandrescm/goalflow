using NetArchTest.Rules;

namespace GoalFlow.Tests.Architecture;

/// <summary>
/// Spec 004 T-37 (R-46, ADR-03 §5): la FrameworkReference no autoriza tipos de ASP.NET Core en Infrastructure.
/// Dos verificaciones, ninguna filtrada por namespace (C-21: M17 sobrevivió a un filtro "GoalFlow").
/// </summary>
public class InfrastructureSinAspNetCoreTests
{
    private static readonly System.Reflection.Assembly Infrastructure = GoalFlow.Infrastructure.AssemblyReference.Assembly;
    private static readonly System.Reflection.Assembly Pruebas = typeof(InfrastructureSinAspNetCoreTests).Assembly;

    // (a) Referencias del ensamblado compilado.

    [Fact]
    public void T37a_el_ensamblado_de_Infrastructure_no_referencia_Microsoft_AspNetCore()
    {
        var resultado = ReglasDeArquitectura.SinReferenciasAAspNetCore(Infrastructure);

        Assert.True(resultado.Cumple, resultado.ToString());
    }

    [Fact]
    public void T37a_canario_el_ensamblado_de_pruebas_si_referencia_Microsoft_AspNetCore()
    {
        var resultado = ReglasDeArquitectura.SinReferenciasAAspNetCore(Pruebas);

        Assert.False(resultado.Cumple, "El canario debía hacer fallar la regla");
        Assert.Contains(resultado.Violaciones, v => v.Contains("Microsoft.AspNetCore"));
    }

    // (b) Tipos: todos, incluidos los del namespace global.

    [Fact]
    public void T37b_ningun_tipo_de_Infrastructure_depende_de_Microsoft_AspNetCore()
    {
        var resultado = ReglasDeArquitectura.InfrastructureSinAspNetCore(
            Types.InAssembly(Infrastructure).That().DoNotHaveName("__ningun_tipo_se_llama_asi__")); // Sin filtro de namespace: incluye el global.

        Assert.True(resultado.Cumple, resultado.ToString());
    }

    [Fact]
    public void T37b_canario_un_tipo_que_usa_HttpContext_hace_fallar_la_regla_y_se_nombra()
    {
        var tipos = Types.InAssembly(Pruebas)
            .That().ResideInNamespace("GoalFlow.Tests.Architecture.Fixtures.InfrastructureConAspNetCore");

        var resultado = ReglasDeArquitectura.InfrastructureSinAspNetCore(tipos);

        Assert.False(resultado.Cumple, "El canario debía hacer fallar la regla");
        Assert.Contains("InfrastructureUsaHttpContext", Assert.Single(resultado.Violaciones));
    }
}
