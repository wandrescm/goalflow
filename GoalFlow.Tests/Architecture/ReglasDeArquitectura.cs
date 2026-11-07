using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using GoalFlow.Application.Mensajeria;
using GoalFlow.Domain.Tenancy;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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
        ReferenciasPermitidas(ensamblado, ReferenciaPermitidaEnApplication);

    /// <summary>
    /// Spec 005 R-36: predicado de T-03 sobre nombres de ENSAMBLADO (nunca namespaces). Acepta Domain, System*
    /// y Microsoft.Extensions.*.Abstractions (p. ej. Microsoft.Extensions.Logging.Abstractions).
    /// </summary>
    public static bool ReferenciaPermitidaEnApplication(string nombreDeEnsamblado) =>
        nombreDeEnsamblado == NombreDomain
        || EsSystem(nombreDeEnsamblado)
        || Regex.IsMatch(nombreDeEnsamblado, @"^Microsoft\.Extensions\..+\.Abstractions$");

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

    /// <summary>
    /// Spec 002 T-02: el grafo aplicado a producción. Los namespaces compartidos no son módulos:
    /// no se evalúan como origen y cualquier módulo puede usarlos como destino.
    /// </summary>
    public static ResultadoRegla GrafoDeModulosEnProduccion(
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> permitidos,
        IEnumerable<Type> tipos,
        IReadOnlyCollection<string> raices,
        IReadOnlyCollection<string> compartidos)
    {
        var conCompartidos = permitidos.ToDictionary(
            fila => fila.Key,
            fila => (IReadOnlyCollection<string>)[.. fila.Value, .. compartidos]);
        var deModulos = tipos.Where(t => ModuloDe(t, raices) is not { } origen || !compartidos.Contains(origen.Modulo));

        return GrafoDeModulos(conCompartidos, deModulos, raices);
    }

    /// <summary>
    /// Spec 005 R-33 (T-24): módulos que la regla de producción evalúa como origen en una capa,
    /// es decir, los primeros segmentos de namespace bajo <paramref name="raiz"/> que no son compartidos.
    /// </summary>
    public static IReadOnlySet<string> ModulosEvaluados(
        IEnumerable<Type> tipos, string raiz, IReadOnlyCollection<string> compartidos) =>
        tipos
            .Select(t => ModuloDe(t, [raiz]))
            .Where(m => m is not null && !compartidos.Contains(m.Value.Modulo))
            .Select(m => m!.Value.Modulo)
            .ToHashSet(StringComparer.Ordinal);

    // ---- Tenancy mínimo (Spec 002 T-01) ----

    /// <summary>Un namespace Tenancy solo admite TenantId, ITenantContext y la excepción de tenant ausente.</summary>
    public static ResultadoRegla TenancyMinimo(IEnumerable<Type> tipos, IReadOnlyCollection<string> permitidos)
    {
        var violaciones = tipos
            .Where(t => t.Namespace is not null
                && t.Namespace.Split('.').Contains("Tenancy")
                && !t.IsDefined(typeof(CompilerGeneratedAttribute), false)
                && !t.IsNested)
            .Where(t => !permitidos.Contains(t.Name))
            .Select(t => $"{t.FullName} no pertenece a Tenancy")
            .ToList();
        return ResultadoRegla.Desde(violaciones);
    }

    // ---- Pipeline post-guardado (Spec 004) ----

    /// <summary>
    /// Spec 004 R-46 (T-37 a): el ensamblado no referencia ninguno cuyo nombre empiece por Microsoft.AspNetCore.
    /// El compilador solo emite referencias a ensamblados usados, así que esto vale sea cual sea el namespace del tipo.
    /// </summary>
    public static ResultadoRegla SinReferenciasAAspNetCore(Assembly ensamblado) =>
        ReferenciasPermitidas(ensamblado, n => !n.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));

    /// <summary>Spec 004 R-46 (T-37): Infrastructure referencia el framework compartido de ASP.NET Core, pero ningún tipo suyo lo usa.</summary>
    public static ResultadoRegla InfrastructureSinAspNetCore(PredicateList tipos) =>
        SinDependenciasDe(tipos, "Microsoft.AspNetCore");

    private static readonly (string Nombre, Type Tipo)[] PropiedadesDeEvento =
        [("AgregadoId", typeof(Guid)), ("TenantId", typeof(TenantId)), ("Version", typeof(int))];

    /// <summary>
    /// Spec 004 R-39 (T-25): todo evento tiene exactamente las propiedades públicas del contrato, con sus tipos,
    /// y vive en el sub-namespace .Contracts de un módulo.
    /// </summary>
    public static ResultadoRegla ContratosDeEventos(IEnumerable<Type> tipos, IReadOnlyCollection<string> raices)
    {
        var violaciones = new List<string>();
        var eventos = tipos.Where(t => !t.IsInterface && !t.IsAbstract && typeof(IEventoDeIntegracion).IsAssignableFrom(t));

        foreach (var evento in eventos)
        {
            var propiedades = evento.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => (p.Name, Tipo: p.PropertyType))
                .OrderBy(p => p.Name, StringComparer.Ordinal)
                .ToList();

            if (!propiedades.SequenceEqual(PropiedadesDeEvento))
                violaciones.Add($"{evento.FullName}: sus propiedades públicas ({string.Join(", ", propiedades.Select(p => $"{p.Tipo.Name} {p.Name}"))}) " +
                    "no son exactamente AgregadoId (Guid), TenantId y Version (int)");

            if (ModuloDe(evento, raices) is not { EsContracts: true })
                violaciones.Add($"{evento.FullName}: un evento debe vivir en el sub-namespace .Contracts de un módulo");
        }

        return ResultadoRegla.Desde(violaciones);
    }

    /// <summary>Spec 004 R-40 (T-26): toda entidad del modelo con TenantId tiene el filtro con nombre Tenant.</summary>
    public static ResultadoRegla EntidadesConTenantTienenFiltro(IModel modelo, IReadOnlyCollection<string> excepciones)
    {
        var violaciones = modelo.GetEntityTypes()
            .Where(e => e.FindProperty("TenantId") is not null)
            .Where(e => !excepciones.Contains(e.ClrType.Name))
            .Where(e => e.FindDeclaredQueryFilter("Tenant") is null)
            .Select(e => $"{e.ClrType.Name} tiene TenantId y no tiene el filtro Tenant")
            .ToList();
        return ResultadoRegla.Desde(violaciones);
    }

    public sealed record FilaDeCatalogo(string Tabla, bool RlsActivo, long Politicas);

    /// <summary>Spec 004 R-41 (T-27): toda tabla con tenant_id tiene RLS y al menos una política.</summary>
    public static ResultadoRegla TablasConTenantTienenRls(IEnumerable<FilaDeCatalogo> tablas)
    {
        var violaciones = new List<string>();
        foreach (var tabla in tablas)
        {
            if (!tabla.RlsActivo) violaciones.Add($"{tabla.Tabla} tiene tenant_id y no tiene RLS");
            else if (tabla.Politicas == 0) violaciones.Add($"{tabla.Tabla} tiene RLS pero ninguna política");
        }

        return ResultadoRegla.Desde(violaciones);
    }

    /// <summary>Spec 004 R-42 (T-28): una caché de aplicación registrada exige un SizeLimit explícito.</summary>
    public static ResultadoRegla CachesConLimite(IServiceCollection registros, IServiceProvider proveedor)
    {
        if (!registros.Any(d => d.ServiceType == typeof(IMemoryCache))) return ResultadoRegla.Desde([]);

        var limite = proveedor.GetRequiredService<IOptions<MemoryCacheOptions>>().Value.SizeLimit;
        return ResultadoRegla.Desde(limite is null ? ["IMemoryCache está registrada sin SizeLimit"] : []);
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
