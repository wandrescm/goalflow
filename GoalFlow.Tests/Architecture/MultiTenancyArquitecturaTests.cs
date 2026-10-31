using System.Reflection;
using GoalFlow.Application.Tenancy;
using GoalFlow.Domain.Tenancy;

namespace GoalFlow.Tests.Architecture;

/// <summary>Spec 002 T-01 y T-02 sobre producción y sus canarios (T-03 está en CanariosTests).</summary>
public class MultiTenancyArquitecturaTests
{
    private const string RaizFixtures = "GoalFlow.Tests.Architecture.Fixtures";
    private static readonly Assembly EnsambladoDePruebas = typeof(MultiTenancyArquitecturaTests).Assembly;

    private static readonly Assembly[] Produccion =
    [
        GoalFlow.Domain.AssemblyReference.Assembly,
        GoalFlow.Application.AssemblyReference.Assembly,
        GoalFlow.Infrastructure.AssemblyReference.Assembly,
        GoalFlow.Api.AssemblyReference.Assembly,
    ];

    private static readonly string[] TiposDeTenancy =
        [nameof(TenantId), nameof(ITenantContext), nameof(TenantAusenteException)];

    private static IEnumerable<Type> TiposEn(string raiz) =>
        EnsambladoDePruebas.GetTypes().Where(t => t.Namespace?.StartsWith(raiz, StringComparison.Ordinal) == true);

    [Fact] // Spec 002 T-01
    public void Tenancy_contiene_solo_sus_tres_tipos()
    {
        var tipos = Produccion.SelectMany(a => a.GetTypes()).ToList();
        var resultado = ReglasDeArquitectura.TenancyMinimo(tipos, TiposDeTenancy);

        Assert.True(resultado.Cumple, resultado.ToString());
        Assert.Equal(
            TiposDeTenancy.Order(),
            tipos.Where(t => t.Namespace?.EndsWith(".Tenancy", StringComparison.Ordinal) == true && !t.IsNested)
                .Select(t => t.Name).Order());
    }

    [Fact] // Spec 002 T-01 (canario)
    public void T01_canario_Tenancy_con_tipo_adicional()
    {
        var resultado = ReglasDeArquitectura.TenancyMinimo(
            TiposEn($"{RaizFixtures}.TenancyCrecido"), TiposDeTenancy);

        Assert.False(resultado.Cumple, "El canario debía hacer fallar la regla");
        Assert.Contains("TipoQueNoVaEnTenancy", Assert.Single(resultado.Violaciones));
    }

    [Fact] // Spec 002 T-02
    public void Tabla_de_modulos_tiene_Ligas_y_produccion_respeta_el_grafo()
    {
        Assert.Equal(["Tenancy"], TablaDeModulos.Adr03["Ligas"]);

        var resultado = ReglasDeArquitectura.GrafoDeModulosEnProduccion(
            TablaDeModulos.Adr03,
            Produccion.SelectMany(a => a.GetTypes()),
            TablaDeModulos.RaicesDeCapa,
            TablaDeModulos.NamespacesCompartidos);

        Assert.True(resultado.Cumple, resultado.ToString());
    }

    [Fact] // Spec 002 T-02 (canario)
    public void T02_canario_Torneos_usa_entidad_de_Ligas()
    {
        var raiz = $"{RaizFixtures}.GrafoConLigas";

        var resultado = ReglasDeArquitectura.GrafoDeModulosEnProduccion(
            TablaDeModulos.Adr03, TiposEn(raiz), [raiz], TablaDeModulos.NamespacesCompartidos);

        Assert.False(resultado.Cumple, "El canario debía hacer fallar la regla");
        Assert.Contains("TorneosViolaPorLigas", Assert.Single(resultado.Violaciones));
    }
}
