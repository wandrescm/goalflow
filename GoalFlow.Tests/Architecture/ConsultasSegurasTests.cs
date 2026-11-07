using System.Reflection;
using GoalFlow.Tests.Architecture.Fixtures.ConsultasProhibidas;

namespace GoalFlow.Tests.Architecture;

/// <summary>Spec 005 T-18 a T-22: reglas de consultas seguras (R-29 a R-32).</summary>
public class ConsultasSegurasTests
{
    private static readonly Assembly EnsambladoDePruebas = typeof(ConsultasSegurasTests).Assembly;

    private static readonly Assembly[] Produccion =
    [
        GoalFlow.Domain.AssemblyReference.Assembly,
        GoalFlow.Application.AssemblyReference.Assembly,
        GoalFlow.Infrastructure.AssemblyReference.Assembly,
        GoalFlow.Api.AssemblyReference.Assembly,
    ];

    private static ResultadoDeConsultas AnalizarFixture(Type caso) =>
        ReglasDeConsultas.Analizar(ReglasDeConsultas.TiposEnNamespace(EnsambladoDePruebas, caso.Namespace!));

    [Fact] // T-18
    public void Produccion_no_usa_consultas_inseguras_fuera_de_la_lista_permitida()
    {
        var resultado = ReglasDeConsultas.Analizar(ReglasDeConsultas.CargarTipos(Produccion));

        Assert.True(resultado.Violaciones.Count == 0, resultado.ToString());
    }

    [Fact] // T-19
    public void Sitios_permitidos_encontrados_son_exactamente_los_esperados()
    {
        var resultado = ReglasDeConsultas.Analizar(ReglasDeConsultas.CargarTipos(Produccion));

        var encontrados = resultado.Permitidos
            .Select(s => (Tipo: s.TipoExterno, s.Familia))
            .ToHashSet();
        var esperados = new HashSet<(string, FamiliaDeConsulta)>
        {
            (ReglasDeConsultas.TipoDelInterceptor, FamiliaDeConsulta.F3),
            ("GoalFlow.Infrastructure.Persistencia.Migraciones.Inicial", FamiliaDeConsulta.F4),
            ("GoalFlow.Infrastructure.Persistencia.Migraciones.EntregasEventos", FamiliaDeConsulta.F4),
        };

        Assert.True(esperados.SetEquals(encontrados),
            "Sitios permitidos: " + string.Join(", ", encontrados.OrderBy(e => e.Tipo)));

        // La máquina de estado async de TransactionStartedAsync cuenta: el escáner debe entrar en tipos anidados.
        Assert.Contains(resultado.Permitidos, s =>
            s.Familia == FamiliaDeConsulta.F3
            && s.TipoExterno == ReglasDeConsultas.TipoDelInterceptor
            && s.TipoContenedor != s.TipoExterno
            && s.TipoContenedor.Contains("TransactionStartedAsync", StringComparison.Ordinal));
    }

    [Theory] // T-20
    [InlineData(typeof(Fixtures.ConsultasProhibidas.F1Metodo.Caso), FamiliaDeConsulta.F1)]
    [InlineData(typeof(Fixtures.ConsultasProhibidas.F1Async.Caso), FamiliaDeConsulta.F1)]
    [InlineData(typeof(Fixtures.ConsultasProhibidas.F1LambdaSinCapturas.Caso), FamiliaDeConsulta.F1)]
    [InlineData(typeof(Fixtures.ConsultasProhibidas.F1LambdaConCaptura.Caso), FamiliaDeConsulta.F1)]
    [InlineData(typeof(Fixtures.ConsultasProhibidas.F1LambdaSoloThis.Caso), FamiliaDeConsulta.F1)]
    [InlineData(typeof(Fixtures.ConsultasProhibidas.F1FuncionLocal.Caso), FamiliaDeConsulta.F1)]
    [InlineData(typeof(Fixtures.ConsultasProhibidas.F2Metodo.Caso), FamiliaDeConsulta.F2)]
    [InlineData(typeof(Fixtures.ConsultasProhibidas.F2Async.Caso), FamiliaDeConsulta.F2)]
    [InlineData(typeof(Fixtures.ConsultasProhibidas.F2LambdaSinCapturas.Caso), FamiliaDeConsulta.F2)]
    [InlineData(typeof(Fixtures.ConsultasProhibidas.F2LambdaConCaptura.Caso), FamiliaDeConsulta.F2)]
    [InlineData(typeof(Fixtures.ConsultasProhibidas.F2LambdaSoloThis.Caso), FamiliaDeConsulta.F2)]
    [InlineData(typeof(Fixtures.ConsultasProhibidas.F2FuncionLocal.Caso), FamiliaDeConsulta.F2)]
    [InlineData(typeof(Fixtures.ConsultasProhibidas.F3NpgsqlCommand.Caso), FamiliaDeConsulta.F3)]
    [InlineData(typeof(Fixtures.ConsultasProhibidas.F3DbCommand.Caso), FamiliaDeConsulta.F3)]
    [InlineData(typeof(Fixtures.ConsultasProhibidas.F4SinMigration.Caso), FamiliaDeConsulta.F4)]
    [InlineData(typeof(Fixtures.ConsultasProhibidas.F1ArbolDeExpresion.Caso), FamiliaDeConsulta.F1)]
    [InlineData(typeof(Fixtures.ConsultasProhibidas.F3GrupoDeMetodos.Caso), FamiliaDeConsulta.F3)]
    [InlineData(typeof(Fixtures.ConsultasProhibidas.F3Interfaz.Caso), FamiliaDeConsulta.F3)]
    [InlineData(typeof(Fixtures.ConsultasProhibidas.F3ConstructorNpgsql.Caso), FamiliaDeConsulta.F3)]
    public void Canario_de_llamada_prohibida_produce_exactamente_una_violacion(Type caso, FamiliaDeConsulta familia)
    {
        var resultado = AnalizarFixture(caso);

        var violacion = Assert.Single(resultado.Violaciones);
        Assert.Equal(caso.FullName, violacion.TipoExterno);
        Assert.Equal(familia, violacion.Familia);
        Assert.Empty(resultado.Permitidos);
    }

    [Theory] // T-21
    [InlineData(typeof(Fixtures.ConsultasProhibidas.PermitidasPorNamespace.Migraciones.NoEsMigration), FamiliaDeConsulta.F4)]
    [InlineData(typeof(Fixtures.ConsultasProhibidas.PermitidasPorNombre.InterceptorDeTransacciones), FamiliaDeConsulta.F3)]
    public void La_lista_permitida_no_se_decide_por_namespace_ni_por_nombre_simple(Type caso, FamiliaDeConsulta familia)
    {
        var resultado = AnalizarFixture(caso);

        var violacion = Assert.Single(resultado.Violaciones);
        Assert.Equal(caso.FullName, violacion.TipoExterno);
        Assert.Equal(familia, violacion.Familia);
        Assert.Empty(resultado.Permitidos);
    }

    [Fact] // T-22
    public void Uso_legitimo_de_EF_Core_no_produce_violaciones()
    {
        var resultado = AnalizarFixture(typeof(Fixtures.ConsultasProhibidas.UsoLegitimo.Caso));

        Assert.Empty(resultado.Violaciones);
        Assert.Empty(resultado.Permitidos);
    }
}
