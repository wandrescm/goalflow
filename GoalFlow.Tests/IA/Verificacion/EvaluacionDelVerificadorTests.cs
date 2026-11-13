using System.Text;
using GoalFlow.Application.IA.Verificacion;
using Xunit.Abstractions;

namespace GoalFlow.Tests.IA.Verificacion;

/// <summary>Una evaluación del verificador sobre un conjunto de casos: matriz por etiqueta, falsos positivos y falsos negativos por separado (Spec 008 R-20).</summary>
internal sealed class EvaluacionDeUnConjunto
{
    public EvaluacionDeUnConjunto(string nombre, IReadOnlyList<CasoDelCorpus> casos)
    {
        Nombre = nombre;
        foreach (var caso in casos)
        {
            var aprobado = VerificadorDeResumen.Verificar(caso.Texto, CorpusV1.Entrada(caso.Fixture)).Aprobado;
            Veredictos[caso.Id] = aprobado;
            Matriz[(caso.Etiqueta, aprobado)] = Matriz.GetValueOrDefault((caso.Etiqueta, aprobado)) + 1;

            // Un texto "fiel" rechazado es un falso positivo; uno "ambiguo" o "no_fiel" aprobado es un falso negativo.
            if (caso.Etiqueta == Etiqueta.Fiel && !aprobado) FalsosPositivos.Add(caso.Id);
            if (caso.Etiqueta != Etiqueta.Fiel && aprobado) FalsosNegativos.Add(caso.Id);
        }
    }

    public string Nombre { get; }
    public Dictionary<string, bool> Veredictos { get; } = [];
    public Dictionary<(Etiqueta, bool), int> Matriz { get; } = [];
    public List<string> FalsosPositivos { get; } = [];
    public List<string> FalsosNegativos { get; } = [];

    public int Aprobados(Etiqueta etiqueta) => Matriz.GetValueOrDefault((etiqueta, true));

    public int Rechazados(Etiqueta etiqueta) => Matriz.GetValueOrDefault((etiqueta, false));

    public string Informe()
    {
        var s = new StringBuilder();
        s.AppendLine($"{Nombre}");
        s.AppendLine("| Label | Approved | Rejected |");
        s.AppendLine("| --- | --- | --- |");
        foreach (var (etiqueta, nombre) in new[] { (Etiqueta.Fiel, "fiel"), (Etiqueta.Ambiguo, "ambiguo"), (Etiqueta.NoFiel, "no_fiel") })
            s.AppendLine($"| {nombre} | {Aprobados(etiqueta)} | {Rechazados(etiqueta)} |");
        s.AppendLine($"False positives: {FalsosPositivos.Count} ({string.Join(", ", FalsosPositivos)})");
        s.AppendLine($"False negatives: {FalsosNegativos.Count} ({string.Join(", ", FalsosNegativos)})");
        return s.ToString();
    }
}

/// <summary>Spec 008 T-02 (R-20): la evaluación del verificador v1 sobre el corpus real y sobre las mutaciones, con las etiquetas finales del Apéndice A.</summary>
public class EvaluacionDelVerificadorTests(ITestOutputHelper salida)
{
    [Fact]
    public void T02_las_matrices_del_corpus_real_y_de_las_mutaciones_son_exactamente_las_de_A3()
    {
        var reales = new EvaluacionDeUnConjunto("Real corpus (14 texts), corpus-v1, verifier v1", CorpusV1.Reales);
        var mutaciones = new EvaluacionDeUnConjunto("Adversarial mutations (12), corpus-v1, verifier v1", CorpusV1.Mutaciones);

        salida.WriteLine(reales.Informe());
        salida.WriteLine(mutaciones.Informe());

        // A.3, corpus real: fiel 10/0, ambiguo 0/2 (M4, M9), no_fiel 0/2 (M6, M8); FP 0, FN 0.
        Assert.Equal((10, 0), (reales.Aprobados(Etiqueta.Fiel), reales.Rechazados(Etiqueta.Fiel)));
        Assert.Equal((0, 2), (reales.Aprobados(Etiqueta.Ambiguo), reales.Rechazados(Etiqueta.Ambiguo)));
        Assert.Equal((0, 2), (reales.Aprobados(Etiqueta.NoFiel), reales.Rechazados(Etiqueta.NoFiel)));
        Assert.Equal(["M4", "M9"], CorpusV1.Reales.Where(c => c.Etiqueta == Etiqueta.Ambiguo).Select(c => c.Id).ToArray());
        Assert.Equal(["M6", "M8"], CorpusV1.Reales.Where(c => c.Etiqueta == Etiqueta.NoFiel).Select(c => c.Id).ToArray());
        Assert.Empty(reales.FalsosPositivos);
        Assert.Empty(reales.FalsosNegativos);

        // A.3, mutaciones: fiel 0/1 (a11), ambiguo 0/1 (a9), no_fiel 2/8 (a4, a8 aprobadas); FP 1 (a11), FN 2 (a4, a8).
        Assert.Equal((0, 1), (mutaciones.Aprobados(Etiqueta.Fiel), mutaciones.Rechazados(Etiqueta.Fiel)));
        Assert.Equal((0, 1), (mutaciones.Aprobados(Etiqueta.Ambiguo), mutaciones.Rechazados(Etiqueta.Ambiguo)));
        Assert.Equal((2, 8), (mutaciones.Aprobados(Etiqueta.NoFiel), mutaciones.Rechazados(Etiqueta.NoFiel)));
        Assert.Equal(["a11"], mutaciones.FalsosPositivos.ToArray());
        Assert.Equal(["a4", "a8"], mutaciones.FalsosNegativos.ToArray());
    }

    [Fact]
    public void T02_la_evaluacion_sale_de_las_etiquetas_y_los_veredictos_y_no_mezcla_falsos_positivos_con_falsos_negativos()
    {
        // Si se cambia una etiqueta, el informe cambia solo donde corresponde: la evaluación se calcula, no se copia.
        var fiel = CorpusV1.Reales.Single(c => c.Id == "M4") with { Etiqueta = Etiqueta.Fiel }; // M4 se rechaza: ahora es un falso positivo
        var noFiel = CorpusV1.Reales.Single(c => c.Id == "M1") with { Etiqueta = Etiqueta.NoFiel }; // M1 se aprueba: ahora es un falso negativo

        var evaluacion = new EvaluacionDeUnConjunto("sintética", [fiel, noFiel]);

        Assert.Equal(["M4"], evaluacion.FalsosPositivos.ToArray());
        Assert.Equal(["M1"], evaluacion.FalsosNegativos.ToArray());
        Assert.Contains("False positives: 1 (M4)", evaluacion.Informe());
        Assert.Contains("False negatives: 1 (M1)", evaluacion.Informe());
    }
}
