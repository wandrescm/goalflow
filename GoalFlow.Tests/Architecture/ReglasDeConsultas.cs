using System.Reflection;
using System.Runtime.InteropServices;
using Mono.Cecil;

namespace GoalFlow.Tests.Architecture;

public enum FamiliaDeConsulta
{
    F1,
    F2,
    F3,
    F4,
}

/// <summary>Una instrucción de llamada a un miembro de una familia vigilada.</summary>
/// <param name="TipoExterno">Tipo contenedor más externo (los anidados se atribuyen a él).</param>
/// <param name="TipoContenedor">Tipo que contiene realmente el método (puede ser anidado).</param>
public sealed record SitioDeConsulta(
    string TipoExterno,
    string TipoContenedor,
    string Metodo,
    FamiliaDeConsulta Familia,
    string MiembroLlamado)
{
    public override string ToString() => $"{TipoExterno} · {Metodo} · {Familia} · {MiembroLlamado}";
}

public sealed record ResultadoDeConsultas(
    IReadOnlyList<SitioDeConsulta> Violaciones,
    IReadOnlyList<SitioDeConsulta> Permitidos)
{
    public override string ToString() =>
        Violaciones.Count == 0 ? "Sin violaciones" : "Violaciones: " + string.Join("; ", Violaciones);
}

/// <summary>
/// Spec 005 R-29 a R-31: escáner de IL sobre consultas inseguras. Las pruebas reales y los canarios
/// invocan exactamente <see cref="Analizar"/>.
/// </summary>
public static class ReglasDeConsultas
{
    public const string TipoDelInterceptor = "GoalFlow.Infrastructure.Persistencia.InterceptorDeTransacciones";

    /// <summary>Carga los tipos (anidados incluidos) de los ensamblados. Si Cecil no resuelve algo, lanza.</summary>
    public static IReadOnlyList<TypeDefinition> CargarTipos(params Assembly[] ensamblados)
    {
        var resolutor = new DefaultAssemblyResolver();
        resolutor.AddSearchDirectory(RuntimeEnvironment.GetRuntimeDirectory());
        resolutor.AddSearchDirectory(Path.GetDirectoryName(typeof(Microsoft.AspNetCore.Builder.WebApplication).Assembly.Location)!);

        var tipos = new List<TypeDefinition>();
        foreach (var ensamblado in ensamblados)
        {
            resolutor.AddSearchDirectory(Path.GetDirectoryName(ensamblado.Location)!);
            var modulo = ModuleDefinition.ReadModule(ensamblado.Location, new ReaderParameters { AssemblyResolver = resolutor });
            tipos.AddRange(modulo.GetTypes());
        }

        return tipos;
    }

    /// <summary>Tipos del módulo cuyo tipo más externo vive exactamente en el namespace indicado.</summary>
    public static IReadOnlyList<TypeDefinition> TiposEnNamespace(Assembly ensamblado, string espacio) =>
        [.. CargarTipos(ensamblado).Where(t => TipoExterno(t).Namespace == espacio)];

    public static TypeDefinition TipoExterno(TypeDefinition tipo)
    {
        while (tipo.DeclaringType is not null) tipo = tipo.DeclaringType;
        return tipo;
    }

    // STUB (commit 3): devuelve un resultado vacío. La implementación llega en el commit siguiente.
    public static ResultadoDeConsultas Analizar(IEnumerable<TypeDefinition> tipos) =>
        new([], []);
}
