namespace GoalFlow.Tests.Architecture;

/// <summary>Spec 011 T-05 (R-16) y T-14 (R-28 a R-32): la publicación la decide solo el selector, y el grafo de módulos no crece.</summary>
public class PublicacionArquitecturaTests
{
    private static IReadOnlyList<Mono.Cecil.TypeDefinition> Produccion() =>
        ReglasDeConsultas.CargarTipos(
            GoalFlow.Application.AssemblyReference.Assembly, typeof(GoalFlow.Api.Hosting.LecturasPublicasEndpoints).Assembly);

    [Fact]
    public void T05_el_caso_de_uso_del_detalle_y_su_endpoint_solo_publican_por_el_selector()
    {
        var evaluados = ReglasDePublicacion.TiposEvaluados(Produccion());

        // La selección se afirma: exactamente esos dos tipos externos, con sus anidados y generados por el compilador.
        Assert.Equal(
            [ReglasDePublicacion.Endpoints, ReglasDePublicacion.CasoDeUso],
            evaluados.Select(t => ReglasDeConsultas.TipoExterno(t).FullName).Distinct().Order(StringComparer.Ordinal).ToArray());
        Assert.Contains(evaluados, t => t.DeclaringType is not null); // los anidados (lambdas, máquinas de estado) entran

        // Anti-vacío: el caso de uso referencia el selector.
        Assert.True(
            ReglasDePublicacion.ReferenciaAlSelector(evaluados.Where(t => ReglasDeConsultas.TipoExterno(t).FullName == ReglasDePublicacion.CasoDeUso)),
            "El caso de uso del detalle debe llamar a SelectorDePublicacion.Seleccionar");

        var resultado = ReglasDePublicacion.Analizar(evaluados);

        Assert.True(resultado.Cumple, resultado.ToString());
    }

    [Fact]
    public void T05_canario_un_tipo_que_filtra_filas_por_Estado_hace_fallar_la_regla()
    {
        var tipos = ReglasDeConsultas.TiposEnNamespace(typeof(PublicacionArquitecturaTests).Assembly, ReglasDePublicacion.EspacioDelCanario);
        Assert.NotEmpty(tipos);

        var resultado = ReglasDePublicacion.Analizar(tipos);

        Assert.False(resultado.Cumple, "El canario debía hacer fallar la regla");
        Assert.Contains(resultado.Violaciones, v => v.Contains("get_Estado", StringComparison.Ordinal)); // la lambda anidada filtra por Estado
        Assert.Contains(resultado.Violaciones, v => v.Contains("VisibilidadDeResumen", StringComparison.Ordinal));
    }

    [Fact] // T-14 (verde inicial): TablaDeModulos no cambia (R-29: 0 filas nuevas)
    public void T14_la_tabla_de_modulos_es_la_de_ADR_03_sin_filas_nuevas()
    {
        var esperada = new Dictionary<string, string[]>
        {
            ["Tenancy"] = [],
            ["Ligas"] = ["Tenancy"],
            ["Torneos"] = ["Tenancy"],
            ["Estadisticas"] = ["Tenancy", "Torneos.Contracts"],
            ["Pollas"] = ["Tenancy", "Torneos.Contracts", "Estadisticas.Contracts"],
            ["IA"] = ["Tenancy", "Torneos.Contracts", "Estadisticas.Contracts"],
        };

        Assert.Equal(esperada.Keys.Order(), TablaDeModulos.Adr03.Keys.Order());
        foreach (var (modulo, dependencias) in esperada)
            Assert.Equal(dependencias.Order(), TablaDeModulos.Adr03[modulo].Order());
    }

    [Fact] // T-14: el caso de uso nuevo de IA solo referencia namespaces permitidos
    public void T14_el_caso_de_uso_del_detalle_solo_referencia_Tenancy_Torneos_Contracts_y_los_compartidos()
    {
        var casoDeUso = ReglasDePublicacion.TiposEvaluados(Produccion())
            .Where(t => ReglasDeConsultas.TipoExterno(t).FullName == ReglasDePublicacion.CasoDeUso).ToList();
        Assert.NotEmpty(casoDeUso);

        var referidos = ReglasDePublicacion.NamespacesDeGoalFlow(casoDeUso);

        string[] permitidos =
        [
            "GoalFlow.Application.IA", "GoalFlow.Application.IA.Verificacion", "GoalFlow.Application.Tenancy",
            "GoalFlow.Application.Torneos.Contracts", "GoalFlow.Application.Persistencia", "GoalFlow.Domain.IA", "GoalFlow.Domain.Tenancy",
        ];
        Assert.Empty(referidos.Except(permitidos));
        Assert.Contains("GoalFlow.Application.Torneos.Contracts", referidos); // control positivo: la regla ve las referencias
    }
}
