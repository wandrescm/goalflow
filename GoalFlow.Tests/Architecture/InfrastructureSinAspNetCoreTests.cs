using NetArchTest.Rules;

namespace GoalFlow.Tests.Architecture;

/// <summary>Spec 004 T-37 (R-46, ADR-03 §5): la FrameworkReference no autoriza tipos de ASP.NET Core en Infrastructure.</summary>
public class InfrastructureSinAspNetCoreTests
{
    [Fact]
    public void T37_ningun_tipo_de_Infrastructure_depende_de_Microsoft_AspNetCore()
    {
        var tipos = Types.InAssembly(GoalFlow.Infrastructure.AssemblyReference.Assembly)
            .That().ResideInNamespaceStartingWith("GoalFlow");

        var resultado = ReglasDeArquitectura.InfrastructureSinAspNetCore(tipos);

        Assert.True(resultado.Cumple, resultado.ToString());
    }

    [Fact]
    public void T37_canario_un_tipo_que_usa_HttpContext_hace_fallar_la_regla()
    {
        var tipos = Types.InAssembly(typeof(InfrastructureSinAspNetCoreTests).Assembly)
            .That().ResideInNamespace("GoalFlow.Tests.Architecture.Fixtures.InfrastructureConAspNetCore");

        var resultado = ReglasDeArquitectura.InfrastructureSinAspNetCore(tipos);

        Assert.False(resultado.Cumple, "El canario debía hacer fallar la regla");
        Assert.Contains("InfrastructureUsaHttpContext", Assert.Single(resultado.Violaciones));
    }
}
