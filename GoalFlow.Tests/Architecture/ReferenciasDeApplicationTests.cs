namespace GoalFlow.Tests.Architecture;

/// <summary>Spec 005 T-26 (b), R-36: el caso positivo de la excepción de T-03 lo ejercita el código real.</summary>
public class ReferenciasDeApplicationTests
{
    [Fact]
    public void T26b_Application_referencia_las_abstracciones_de_logging_y_T03_sigue_pasando()
    {
        var application = GoalFlow.Application.AssemblyReference.Assembly;

        // Anti-vacío: el compilador solo emite referencias a ensamblados que se usan.
        Assert.Contains(application.GetReferencedAssemblies(), a => a.Name == "Microsoft.Extensions.Logging.Abstractions");
        var resultado = ReglasDeArquitectura.ApplicationSoloReferenciasPermitidas(application);
        Assert.True(resultado.Cumple, resultado.ToString());
    }
}
