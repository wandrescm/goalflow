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

    private const string NombreMigration = "Microsoft.EntityFrameworkCore.Migrations.Migration";
    private const string NombreMigrationBuilder = "Microsoft.EntityFrameworkCore.Migrations.MigrationBuilder";

    private static readonly HashSet<string> BasesDeF3 =
    [
        "System.Data.Common.DbCommand",
        "System.Data.Common.DbBatch",
        "System.Data.Common.DbBatchCommand",
        "System.Data.IDbCommand",
    ];

    private static readonly HashSet<string> BasesDeFabricas =
    [
        "System.Data.Common.DbConnection",
        "System.Data.Common.DbDataSource",
        "System.Data.IDbConnection",
    ];

    private static readonly HashSet<string> CopiaDeNpgsql =
    [
        "BeginBinaryImport", "BeginBinaryExport", "BeginTextImport", "BeginTextExport", "BeginRawBinaryCopy",
    ];

    /// <summary>
    /// Recorre el cuerpo de todos los métodos de todos los tipos dados (anidados incluidos) y clasifica cada
    /// instrucción call, callvirt, newobj y ldftn hacia un miembro de las familias F1 a F4.
    /// Si Cecil no resuelve un tipo necesario, lanza: nunca se omite.
    /// </summary>
    public static ResultadoDeConsultas Analizar(IEnumerable<TypeDefinition> tipos)
    {
        var violaciones = new List<SitioDeConsulta>();
        var permitidos = new List<SitioDeConsulta>();
        var cache = new Dictionary<string, TypeDefinition>(StringComparer.Ordinal);

        foreach (var tipo in tipos)
        {
            var externo = TipoExterno(tipo);
            foreach (var metodo in tipo.Methods.Where(m => m.HasBody))
            {
                foreach (var instruccion in metodo.Body.Instructions)
                {
                    if (instruccion.OpCode.Code is not (Mono.Cecil.Cil.Code.Call or Mono.Cecil.Cil.Code.Callvirt
                        or Mono.Cecil.Cil.Code.Newobj or Mono.Cecil.Cil.Code.Ldftn
                        or Mono.Cecil.Cil.Code.Ldvirtftn or Mono.Cecil.Cil.Code.Ldtoken))
                        continue;
                    if (instruccion.Operand is not MethodReference llamado) continue;

                    var familia = Clasificar(llamado, cache);
                    if (familia is null) continue;

                    var sitio = new SitioDeConsulta(
                        externo.FullName, tipo.FullName, metodo.Name, familia.Value,
                        $"{llamado.DeclaringType.FullName}::{llamado.Name}");

                    (EstaPermitido(externo, familia.Value, cache) ? permitidos : violaciones).Add(sitio);
                }
            }
        }

        return new ResultadoDeConsultas(violaciones, permitidos);
    }

    private static FamiliaDeConsulta? Clasificar(MethodReference llamado, Dictionary<string, TypeDefinition> cache)
    {
        var declarante = llamado.DeclaringType.FullName;
        var nombre = llamado.Name;

        if (declarante == "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions" && nombre == "IgnoreQueryFilters")
            return FamiliaDeConsulta.F1;

        if (declarante is "Microsoft.EntityFrameworkCore.RelationalQueryableExtensions"
                or "Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions"
            && (nombre.StartsWith("FromSql", StringComparison.Ordinal)
                || nombre.StartsWith("SqlQuery", StringComparison.Ordinal)
                || nombre.StartsWith("ExecuteSql", StringComparison.Ordinal)))
            return FamiliaDeConsulta.F2;

        // Arreglos y parámetros genéricos no pueden heredar de los tipos vigilados.
        if (llamado.DeclaringType is ArrayType or GenericParameter) return null;

        var jerarquia = Jerarquia(llamado.DeclaringType, cache);

        if (nombre == "Sql" && jerarquia.Contains(NombreMigrationBuilder)) return FamiliaDeConsulta.F4;
        if (jerarquia.Overlaps(BasesDeF3)) return FamiliaDeConsulta.F3;
        if ((nombre is "CreateCommand" or "CreateBatch") && jerarquia.Overlaps(BasesDeFabricas)) return FamiliaDeConsulta.F3;
        if (CopiaDeNpgsql.Contains(nombre) && jerarquia.Contains("Npgsql.NpgsqlConnection")) return FamiliaDeConsulta.F3;

        return null;
    }

    private static bool EstaPermitido(TypeDefinition externo, FamiliaDeConsulta familia, Dictionary<string, TypeDefinition> cache) =>
        familia switch
        {
            FamiliaDeConsulta.F4 => Jerarquia(externo, cache).Contains(NombreMigration),
            FamiliaDeConsulta.F3 => externo.FullName == TipoDelInterceptor,
            _ => false,
        };

    /// <summary>Nombres completos del tipo y de todas sus bases. Lanza si algún tipo no se puede resolver.</summary>
    private static HashSet<string> Jerarquia(TypeReference tipo, Dictionary<string, TypeDefinition> cache)
    {
        var nombres = new HashSet<string>(StringComparer.Ordinal);
        TypeReference? actual = tipo;
        while (actual is not null)
        {
            var definicion = Resolver(actual, cache);
            nombres.Add(definicion.FullName);
            actual = definicion.BaseType;
        }

        return nombres;
    }

    private static TypeDefinition Resolver(TypeReference tipo, Dictionary<string, TypeDefinition> cache)
    {
        var elemento = tipo.GetElementType();
        var clave = $"{elemento.Scope?.Name}|{elemento.FullName}";
        if (cache.TryGetValue(clave, out var enCache)) return enCache;

        var definicion = elemento.Resolve()
            ?? throw new InvalidOperationException($"Cecil no pudo resolver {elemento.FullName} ({elemento.Scope?.Name}); la regla no puede omitirlo.");
        cache[clave] = definicion;
        return definicion;
    }
}
