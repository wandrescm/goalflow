namespace GoalFlow.Tests.Architecture;

/// <summary>Spec 010 T-21 (R-22): la IP del cliente solo la decide la función de partición del limitador.</summary>
public class IpDelClienteArquitecturaTests
{
    private const string EspacioDeLosCanarios = "GoalFlow.Tests.Architecture.Fixtures.IpDelCliente";

    private static IReadOnlyList<Mono.Cecil.TypeDefinition> TiposDeProduccion() =>
        ReglasDeConsultas.CargarTipos(
            GoalFlow.Api.AssemblyReference.Assembly,
            GoalFlow.Application.AssemblyReference.Assembly,
            GoalFlow.Infrastructure.AssemblyReference.Assembly,
            GoalFlow.Domain.AssemblyReference.Assembly);

    private static ResultadoRegla AnalizarCanario(string tipo)
    {
        var tipos = ReglasDeConsultas.TiposEnNamespace(typeof(IpDelClienteArquitecturaTests).Assembly, EspacioDeLosCanarios)
            .Where(t => t.Name == tipo)
            .ToList();
        Assert.NotEmpty(tipos);
        return ReglasDeIpDelCliente.Analizar(tipos);
    }

    [Fact]
    public void T21_en_produccion_ningun_tipo_fuera_de_la_particion_nombra_el_encabezado_ni_lee_RemoteIpAddress()
    {
        var resultado = ReglasDeIpDelCliente.Analizar(TiposDeProduccion());

        Assert.True(resultado.Cumple, resultado.ToString());
    }

    [Fact]
    public void T21_alcance_la_regla_recorre_ParticionDeFrecuencia_y_solo_ese_tipo_nombra_el_encabezado()
    {
        var tipos = TiposDeProduccion();

        // Anti-vacío: si el tipo no estuviera en el conjunto, la regla pasaría sin mirar nada.
        Assert.Contains(tipos, t => t.FullName == ReglasDeIpDelCliente.TipoDeLaParticion);
        Assert.Contains(tipos, t => t.Namespace.StartsWith("GoalFlow.Application", StringComparison.Ordinal));
        Assert.Contains(tipos, t => t.Namespace.StartsWith("GoalFlow.Infrastructure", StringComparison.Ordinal));
        Assert.Contains(tipos, t => t.Namespace.StartsWith("GoalFlow.Domain", StringComparison.Ordinal));

        // Y la regla sí ve el literal donde está.
        Assert.Equal([ReglasDeIpDelCliente.TipoDeLaParticion], ReglasDeIpDelCliente.TiposQueNombranElEncabezado(tipos));
    }

    [Fact]
    public void T21_canario_a_leer_el_encabezado_por_su_nombre_hace_fallar_solo_la_regla_a()
    {
        var resultado = AnalizarCanario("LecturaDeEncabezado");

        Assert.False(resultado.Cumple, "El canario debía hacer fallar la regla (a)");
        var violacion = Assert.Single(resultado.Violaciones);
        Assert.Contains("(a)", violacion, StringComparison.Ordinal);
        Assert.Contains("LecturaDeEncabezado", violacion, StringComparison.Ordinal);
    }

    [Fact]
    public void T21_canario_b_leer_RemoteIpAddress_hace_fallar_solo_la_regla_b()
    {
        var resultado = AnalizarCanario("LecturaDeRemoteIp");

        Assert.False(resultado.Cumple, "El canario debía hacer fallar la regla (b)");
        var violacion = Assert.Single(resultado.Violaciones);
        Assert.Contains("(b)", violacion, StringComparison.Ordinal);
        Assert.Contains("LecturaDeRemoteIp", violacion, StringComparison.Ordinal);
    }
}
