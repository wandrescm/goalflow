using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using NetArchTest.Rules;

namespace GoalFlow.Tests.Architecture;

/// <summary>
/// Reglas de ADR-03. Los tests reales y los canarios invocan exactamente estas funciones.
/// </summary>
public static class ReglasDeArquitectura
{
    public const string NombreDomain = "GoalFlow.Domain";
    public const string NombreApplication = "GoalFlow.Application";
    public const string NombreInfrastructure = "GoalFlow.Infrastructure";
    public const string NombreApi = "GoalFlow.Api";

    // ---- Reglas sobre referencias de ensamblado (T-01, T-03, T-06, T-07) ----

    /// <summary>T-01: Domain solo referencia System* y netstandard.</summary>
    public static ResultadoRegla DomainSoloReferenciasBase(Assembly ensamblado) =>
        ReferenciasPermitidas(ensamblado, n => EsSystem(n) || n == "netstandard");

    /// <summary>T-03: Application solo referencia Domain, System* y Microsoft.Extensions.*.Abstractions.</summary>
    public static ResultadoRegla ApplicationSoloReferenciasPermitidas(Assembly ensamblado) =>
        ReferenciasPermitidas(ensamblado, n =>
            n == NombreDomain
            || EsSystem(n)
            || Regex.IsMatch(n, @"^Microsoft\.Extensions\..+\.Abstractions$"));

    /// <summary>T-06: el ensamblado de origen referencia al de destino.</summary>
    public static ResultadoRegla Referencia(Assembly origen, string nombreDestino)
    {
        var referencia = origen.GetReferencedAssemblies().Any(a => a.Name == nombreDestino);
        return ResultadoRegla.Desde(referencia
            ? []
            : [$"{origen.GetName().Name} no referencia a {nombreDestino}"]);
    }

    /// <summary>T-07: guarda anti-vacío; un ensamblado sin tipos haría pasar cualquier regla.</summary>
    public static ResultadoRegla TieneTipos(Assembly ensamblado)
    {
        var hayTipos = ensamblado.GetTypes().Any(t => !t.IsDefined(typeof(CompilerGeneratedAttribute), false));
        return ResultadoRegla.Desde(hayTipos ? [] : [$"{ensamblado.GetName().Name} no contiene tipos"]);
    }

    // ---- Reglas sobre dependencias de tipos (T-02, T-04, T-05) ----

    /// <summary>T-02: Domain sin EF Core, ASP.NET ni Microsoft.Extensions.</summary>
    public static ResultadoRegla DomainSinFrameworks(PredicateList tipos) =>
        SinDependenciasDe(tipos, "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Microsoft.Extensions");

    /// <summary>T-04: Application sin Infrastructure ni Api.</summary>
    public static ResultadoRegla ApplicationSinCapasExternas(PredicateList tipos) =>
        SinDependenciasDe(tipos, NombreInfrastructure, NombreApi);

    /// <summary>T-05: Infrastructure sin Api.</summary>
    public static ResultadoRegla InfrastructureSinApi(PredicateList tipos) =>
        SinDependenciasDe(tipos, NombreApi);

    // ---- Grafo de módulos (T-09) ----

    /// <summary>
    /// Un módulo es el primer segmento de namespace tras una de las <paramref name="raices"/>
    /// (p. ej. GoalFlow.Domain.Torneos). La entrada "X" del mapa permite cualquier tipo del módulo X;
    /// "X.Contracts" permite solo su sub-namespace Contracts. Un módulo siempre puede usarse a sí mismo.
    /// </summary>
    public static ResultadoRegla GrafoDeModulos(
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> permitidos,
        IEnumerable<Type> tipos,
        IReadOnlyCollection<string> raices)
    {
        var violaciones = new List<string>();
        foreach (var tipo in tipos)
        {
            var origen = ModuloDe(tipo, raices);
            if (origen is null) continue;

            if (!permitidos.TryGetValue(origen.Value.Modulo, out var dependeDe))
            {
                violaciones.Add($"{tipo.FullName}: el módulo {origen.Value.Modulo} no está en la tabla del ADR-03");
                continue;
            }

            foreach (var dependencia in TiposReferenciados(tipo))
            {
                var destino = ModuloDe(dependencia, raices);
                if (destino is null || destino.Value.Modulo == origen.Value.Modulo) continue;

                var permitido = dependeDe.Contains(destino.Value.Modulo)
                    || (destino.Value.EsContracts && dependeDe.Contains(destino.Value.Modulo + ".Contracts"));
                if (!permitido)
                    violaciones.Add($"{tipo.FullName} → {dependencia.FullName}: {origen.Value.Modulo} no puede depender de {destino.Value.Modulo}");
            }
        }

        return ResultadoRegla.Desde(violaciones);
    }

    // ---- Auxiliares ----

    private static ResultadoRegla ReferenciasPermitidas(Assembly ensamblado, Func<string, bool> permitida)
    {
        var violaciones = ensamblado.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(n => !permitida(n))
            .Select(n => $"{ensamblado.GetName().Name} referencia a {n}")
            .ToList();
        return ResultadoRegla.Desde(violaciones);
    }

    private static bool EsSystem(string nombre) =>
        nombre == "System" || nombre.StartsWith("System.", StringComparison.Ordinal);

    private static ResultadoRegla SinDependenciasDe(PredicateList tipos, params string[] prohibidos)
    {
        var resultado = tipos.ShouldNot().HaveDependencyOnAny(prohibidos).GetResult();
        return ResultadoRegla.Desde((resultado.FailingTypeNames ?? []).ToList());
    }

    private static (string Modulo, bool EsContracts)? ModuloDe(Type tipo, IReadOnlyCollection<string> raices)
    {
        var ns = tipo.Namespace;
        if (ns is null) return null;

        foreach (var raiz in raices)
        {
            if (!ns.StartsWith(raiz + ".", StringComparison.Ordinal)) continue;
            var segmentos = ns[(raiz.Length + 1)..].Split('.');
            return (segmentos[0], segmentos.Length > 1 && segmentos[1] == "Contracts");
        }

        return null;
    }

    private static IEnumerable<Type> TiposReferenciados(Type tipo)
    {
        const BindingFlags todo = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        var candidatos = new List<Type>();
        if (tipo.BaseType is not null) candidatos.Add(tipo.BaseType);
        candidatos.AddRange(tipo.GetInterfaces());
        candidatos.AddRange(tipo.GetFields(todo).Select(f => f.FieldType));
        candidatos.AddRange(tipo.GetProperties(todo).Select(p => p.PropertyType));
        foreach (var m in tipo.GetMethods(todo).Cast<MethodBase>().Concat(tipo.GetConstructors(todo)))
        {
            if (m is MethodInfo mi) candidatos.Add(mi.ReturnType);
            candidatos.AddRange(m.GetParameters().Select(p => p.ParameterType));
        }

        return candidatos.SelectMany(Desenvolver).Distinct();
    }

    private static IEnumerable<Type> Desenvolver(Type tipo)
    {
        if (tipo.HasElementType)
        {
            foreach (var t in Desenvolver(tipo.GetElementType()!)) yield return t;
            yield break;
        }

        yield return tipo;
        if (!tipo.IsGenericType) yield break;
        foreach (var argumento in tipo.GetGenericArguments())
            foreach (var t in Desenvolver(argumento)) yield return t;
    }
}
