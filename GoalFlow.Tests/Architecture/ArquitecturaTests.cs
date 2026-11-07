using System.Reflection;
using NetArchTest.Rules;

namespace GoalFlow.Tests.Architecture;

/// <summary>Tests reales T-01..T-07 sobre los ensamblados de producción.</summary>
public class ArquitecturaTests
{
    private static readonly Assembly Domain = GoalFlow.Domain.AssemblyReference.Assembly;
    private static readonly Assembly Application = GoalFlow.Application.AssemblyReference.Assembly;
    private static readonly Assembly Infrastructure = GoalFlow.Infrastructure.AssemblyReference.Assembly;
    private static readonly Assembly Api = GoalFlow.Api.AssemblyReference.Assembly;

    // Spec 005 R-37: todos los tipos del ensamblado, sin filtro de namespace (AreAbstract o no abstracto = todos).
    private static PredicateList TiposDe(Assembly ensamblado) =>
        Types.InAssembly(ensamblado).That().AreAbstract().Or().AreNotAbstract();

    private static void Cumple(ResultadoRegla resultado) => Assert.True(resultado.Cumple, resultado.ToString());

    [Fact] // T-01
    public void Domain_no_referencia_nada_fuera_de_System_y_netstandard() =>
        Cumple(ReglasDeArquitectura.DomainSoloReferenciasBase(Domain));

    [Fact] // T-02
    public void Domain_no_depende_de_EFCore_AspNetCore_ni_Extensions() =>
        Cumple(ReglasDeArquitectura.DomainSinFrameworks(TiposDe(Domain)));

    [Fact] // T-03
    public void Application_solo_referencia_Domain_System_y_Extensions_Abstractions() =>
        Cumple(ReglasDeArquitectura.ApplicationSoloReferenciasPermitidas(Application));

    [Fact] // T-04
    public void Application_no_depende_de_Infrastructure_ni_Api() =>
        Cumple(ReglasDeArquitectura.ApplicationSinCapasExternas(TiposDe(Application)));

    [Fact] // T-05
    public void Infrastructure_no_depende_de_Api() =>
        Cumple(ReglasDeArquitectura.InfrastructureSinApi(TiposDe(Infrastructure)));

    [Fact] // T-06
    public void Infrastructure_referencia_Application_via_AssemblyAppVersion()
    {
        Assert.True(typeof(GoalFlow.Application.IAppVersion).IsAssignableFrom(typeof(GoalFlow.Infrastructure.AssemblyAppVersion)));
        Cumple(ReglasDeArquitectura.Referencia(Infrastructure, ReglasDeArquitectura.NombreApplication));
    }

    [Fact] // T-07
    public void Cada_ensamblado_de_produccion_tiene_al_menos_un_tipo()
    {
        Cumple(ReglasDeArquitectura.TieneTipos(Domain));
        Cumple(ReglasDeArquitectura.TieneTipos(Application));
        Cumple(ReglasDeArquitectura.TieneTipos(Infrastructure));
        Cumple(ReglasDeArquitectura.TieneTipos(Api));
    }
}

/// <summary>Spec 005 T-26 (a): tabla de casos del predicado de T-03.</summary>
public class PredicadoDeApplicationTests
{
    [Theory]
    [InlineData("Microsoft.Extensions.Logging.Abstractions", true)]
    [InlineData("Microsoft.Extensions.Logging", false)]
    [InlineData("Microsoft.Extensions.Logging.Console", false)]
    [InlineData("Microsoft.EntityFrameworkCore", false)]
    [InlineData("Npgsql", false)]
    public void El_predicado_acepta_solo_las_abstracciones_de_logging(string ensamblado, bool esperado) =>
        Assert.Equal(esperado, ReglasDeArquitectura.ReferenciaPermitidaEnApplication(ensamblado));
}
