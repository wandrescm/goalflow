using GoalFlow.Application.Mensajeria;
using GoalFlow.Tests.Architecture;

namespace GoalFlow.Tests.IA;

/// <summary>Spec 007 T-29 (R-35): el módulo IA respeta el grafo, los eventos de producción siguen siendo {PartidoGuardado} y no hay SQL crudo nuevo.</summary>
public class IaArquitecturaTests
{
    private static readonly System.Reflection.Assembly[] Produccion =
    [
        GoalFlow.Domain.AssemblyReference.Assembly,
        GoalFlow.Application.AssemblyReference.Assembly,
        GoalFlow.Infrastructure.AssemblyReference.Assembly,
        GoalFlow.Api.AssemblyReference.Assembly,
    ];

    [Fact]
    public void T29_el_grafo_de_modulos_pasa_y_IA_se_evalua_en_las_tres_capas()
    {
        var tipos = Produccion.SelectMany(a => a.GetTypes()).ToList();

        var resultado = ReglasDeArquitectura.GrafoDeModulosEnProduccion(
            TablaDeModulos.Adr03, tipos, TablaDeModulos.RaicesDeCapa, TablaDeModulos.NamespacesCompartidos);

        Assert.True(resultado.Cumple, resultado.ToString());
        foreach (var capa in new[] { ReglasDeArquitectura.NombreDomain, ReglasDeArquitectura.NombreApplication, ReglasDeArquitectura.NombreInfrastructure })
            Assert.Contains("IA", ReglasDeArquitectura.ModulosEvaluados(tipos, capa, TablaDeModulos.NamespacesCompartidos));
    }

    [Fact]
    public void T29_IA_depende_solo_de_Tenancy_y_Torneos_Contracts_y_la_tabla_de_modulos_no_cambio()
    {
        // ADR-03 §7 ya permitía IA → Torneos.Contracts: TablaDeModulos.cs no se tocó.
        Assert.Equal(["Tenancy", "Torneos.Contracts", "Estadisticas.Contracts"], TablaDeModulos.Adr03["IA"]);
    }

    [Fact]
    public void T29_los_eventos_de_produccion_siguen_siendo_exactamente_PartidoGuardado()
    {
        var tipos = Produccion.SelectMany(a => a.GetTypes()).ToList();

        Assert.True(ReglasDeArquitectura.ContratosDeEventos(tipos, TablaDeModulos.RaicesDeCapa).Cumple);
        Assert.Equal(
            ["PartidoGuardado"],
            tipos.Where(t => !t.IsInterface && !t.IsAbstract && typeof(IEventoDeIntegracion).IsAssignableFrom(t))
                .Select(t => t.Name).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void T29_no_hay_SQL_crudo_nuevo_en_IA_y_el_unico_sitio_permitido_nuevo_es_su_migracion()
    {
        var resultado = ReglasDeConsultas.Analizar(ReglasDeConsultas.CargarTipos(Produccion));

        Assert.True(resultado.Violaciones.Count == 0, resultado.ToString());
        var sitiosDeIa = resultado.Permitidos.Where(s => s.TipoExterno.Contains(".IA", StringComparison.Ordinal)
            || s.TipoExterno.EndsWith("ResumenesDePartido", StringComparison.Ordinal)).ToList();
        Assert.All(sitiosDeIa, s => Assert.Equal("GoalFlow.Infrastructure.Persistencia.Migraciones.ResumenesDePartido", s.TipoExterno));
    }

    [Fact]
    public void T29_la_capa_Application_de_IA_no_referencia_Infrastructure_ni_EF_Core()
    {
        var tiposDeIa = GoalFlow.Application.AssemblyReference.Assembly.GetTypes()
            .Where(t => t.Namespace == "GoalFlow.Application.IA").ToList();

        Assert.NotEmpty(tiposDeIa);
        var referencias = GoalFlow.Application.AssemblyReference.Assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();
        Assert.DoesNotContain("GoalFlow.Infrastructure", referencias);
        Assert.DoesNotContain(referencias, n => n!.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
    }
}
