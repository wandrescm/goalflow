using System.Reflection;
using NetArchTest.Rules;

namespace GoalFlow.Tests.Architecture;

/// <summary>
/// T-08 y T-09: cada regla debe FALLAR frente a una violación deliberada (fixtures).
/// Usan las mismas funciones que los tests reales.
/// </summary>
public class CanariosTests
{
    private const string RaizFixtures = "GoalFlow.Tests.Architecture.Fixtures";
    private static readonly Assembly EnsambladoDePruebas = typeof(CanariosTests).Assembly;

    private static PredicateList Fixture(string espacio) =>
        Types.InAssembly(EnsambladoDePruebas).That().ResideInNamespace($"{RaizFixtures}.{espacio}");

    private static void Falla(ResultadoRegla resultado)
    {
        Assert.False(resultado.Cumple, "El canario debía hacer fallar la regla");
        Assert.NotEmpty(resultado.Violaciones);
    }

    // T-01 y T-03: el ensamblado de pruebas referencia xunit y NetArchTest, que ninguna de las dos reglas admite.
    [Fact]
    public void T01_canario_Domain_con_referencias_no_permitidas() =>
        Falla(ReglasDeArquitectura.DomainSoloReferenciasBase(EnsambladoDePruebas));

    [Theory] // T-02
    [InlineData("DomainConAspNet")]
    [InlineData("DomainConExtensions")]
    public void T02_canario_Domain_con_framework(string espacio) =>
        Falla(ReglasDeArquitectura.DomainSinFrameworks(Fixture(espacio)));

    [Fact]
    public void T03_canario_Application_con_referencias_no_permitidas() =>
        Falla(ReglasDeArquitectura.ApplicationSoloReferenciasPermitidas(EnsambladoDePruebas));

    [Theory] // T-04
    [InlineData("ApplicationConInfrastructure")]
    [InlineData("ApplicationConApi")]
    public void T04_canario_Application_con_capa_externa(string espacio) =>
        Falla(ReglasDeArquitectura.ApplicationSinCapasExternas(Fixture(espacio)));

    [Fact] // T-05
    public void T05_canario_Infrastructure_con_Api() =>
        Falla(ReglasDeArquitectura.InfrastructureSinApi(Fixture("InfrastructureConApi")));

    [Fact] // T-09
    public void T09_canario_grafo_de_modulos_detecta_dependencia_no_permitida()
    {
        var raiz = $"{RaizFixtures}.Modulos";
        var tipos = EnsambladoDePruebas.GetTypes()
            .Where(t => t.Namespace is not null && t.Namespace.StartsWith(raiz, StringComparison.Ordinal));

        var resultado = ReglasDeArquitectura.GrafoDeModulos(TablaDeModulos.Adr03, tipos, [raiz]);

        Falla(resultado);
        var violacion = Assert.Single(resultado.Violaciones);
        Assert.Contains("EstadisticasViolaPorEntidad", violacion);
    }
}
