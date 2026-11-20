using Mono.Cecil;

namespace GoalFlow.Tests.Architecture;

/// <summary>
/// Spec 011 R-16 / T-05: ningún código de la ruta del detalle decide la publicación salvo <c>SelectorDePublicacion</c>. El caso de uso
/// del detalle y la clase de endpoints, con sus tipos anidados y los generados por el compilador (lambdas, máquinas de estado) atribuidos
/// a su tipo externo, no referencian <c>EstadoDeResumen</c>, <c>VisibilidadDeResumen</c> ni <c>VerificadorDeResumen</c>, ni leen los
/// miembros <c>Estado</c>, <c>Huella</c> o <c>MotivoBorrador</c> de <c>ResumenPartido</c>, ni en firmas ni en el IL de los cuerpos.
/// Las pruebas reales y el canario invocan exactamente <see cref="Analizar"/>.
/// </summary>
public static class ReglasDePublicacion
{
    public const string CasoDeUso = "GoalFlow.Application.IA.ConsultaDeDetalleDePartido";
    public const string Endpoints = "GoalFlow.Api.Hosting.LecturasPublicasEndpoints";
    public const string EspacioDelCanario = "GoalFlow.Tests.Architecture.Fixtures.Publicacion";

    private const string Selector = "GoalFlow.Application.IA.SelectorDePublicacion";
    private const string Resumen = "GoalFlow.Domain.IA.ResumenPartido";

    private static readonly string[] TiposProhibidos =
    [
        "GoalFlow.Domain.IA.EstadoDeResumen",
        "GoalFlow.Application.IA.VisibilidadDeResumen",
        "GoalFlow.Application.IA.Verificacion.VerificadorDeResumen",
    ];

    private static readonly string[] MiembrosProhibidos = ["Estado", "Huella", "MotivoBorrador"];

    /// <summary>Los tipos cuyo tipo externo es el caso de uso del detalle o la clase de endpoints.</summary>
    public static IReadOnlyList<TypeDefinition> TiposEvaluados(IEnumerable<TypeDefinition> todos) =>
        [.. todos.Where(t => ReglasDeConsultas.TipoExterno(t).FullName is CasoDeUso or Endpoints)];

    /// <summary>Cada violación dice el tipo que la comete, si está en una firma o en un cuerpo, y lo referido.</summary>
    public static ResultadoRegla Analizar(IEnumerable<TypeDefinition> tipos)
    {
        var violaciones = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var tipo in tipos)
        {
            foreach (var (lugar, referido) in TiposReferidos(tipo))
                if (TiposProhibidos.Contains(referido.FullName))
                    violaciones.Add($"{tipo.FullName} · {lugar} · tipo {referido.FullName}");

            foreach (var (lugar, miembro) in MiembrosReferidos(tipo))
                if (EsMiembroProhibido(miembro))
                    violaciones.Add($"{tipo.FullName} · {lugar} · miembro {miembro.DeclaringType.FullName}::{miembro.Name}");
        }

        return ResultadoRegla.Desde([.. violaciones]);
    }

    /// <summary>Anti-vacío: algún cuerpo de los tipos dados llama a <c>SelectorDePublicacion.Seleccionar</c>.</summary>
    public static bool ReferenciaAlSelector(IEnumerable<TypeDefinition> tipos) =>
        tipos.SelectMany(MiembrosReferidos).Any(m =>
            m.Miembro is MethodReference metodo && Elemento(metodo.DeclaringType).FullName == Selector && metodo.Name == "Seleccionar");

    /// <summary>Los namespaces de GoalFlow que referencian los tipos dados, en firmas y cuerpos (para T-14).</summary>
    public static IReadOnlySet<string> NamespacesDeGoalFlow(IEnumerable<TypeDefinition> tipos)
    {
        var espacios = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tipo in tipos)
        {
            foreach (var (_, referido) in TiposReferidos(tipo)) Agregar(referido);
            foreach (var (_, miembro) in MiembrosReferidos(tipo)) Agregar(miembro.DeclaringType);
        }

        return espacios;

        void Agregar(TypeReference tipo)
        {
            var externo = Elemento(tipo);
            while (externo.DeclaringType is not null) externo = externo.DeclaringType;
            if (externo.Namespace == "GoalFlow" || externo.Namespace.StartsWith("GoalFlow.", StringComparison.Ordinal))
                espacios.Add(externo.Namespace);
        }
    }

    private static bool EsMiembroProhibido(MemberReference miembro)
    {
        if (Elemento(miembro.DeclaringType).FullName != Resumen) return false;
        return MiembrosProhibidos.Any(p => miembro.Name == p || miembro.Name == $"get_{p}" || miembro.Name == $"set_{p}"
            || miembro.Name == $"<{p}>k__BackingField");
    }

    private static TypeReference Elemento(TypeReference tipo) => tipo is TypeSpecification e ? Elemento(e.ElementType) : tipo;

    private static IEnumerable<TypeReference> Desplegar(TypeReference? tipo)
    {
        switch (tipo)
        {
            case null or GenericParameter:
                yield break;
            case GenericInstanceType generico:
                foreach (var x in Desplegar(generico.ElementType)) yield return x;
                foreach (var argumento in generico.GenericArguments)
                    foreach (var x in Desplegar(argumento)) yield return x;
                yield break;
            case TypeSpecification especificacion:
                foreach (var x in Desplegar(especificacion.ElementType)) yield return x;
                yield break;
            default:
                yield return tipo;
                yield break;
        }
    }

    private static IEnumerable<(string Lugar, TypeReference Tipo)> TiposReferidos(TypeDefinition tipo)
    {
        IEnumerable<(string, TypeReference)> Firma(TypeReference? t) => Desplegar(t).Select(x => ("firma", x));
        IEnumerable<(string, TypeReference)> Cuerpo(TypeReference? t) => Desplegar(t).Select(x => ("cuerpo", x));

        foreach (var x in Firma(tipo.BaseType)) yield return x;
        foreach (var i in tipo.Interfaces) foreach (var x in Firma(i.InterfaceType)) yield return x;
        foreach (var f in tipo.Fields) foreach (var x in Firma(f.FieldType)) yield return x;
        foreach (var p in tipo.Properties) foreach (var x in Firma(p.PropertyType)) yield return x;

        foreach (var metodo in tipo.Methods)
        {
            foreach (var x in Firma(metodo.ReturnType)) yield return x;
            foreach (var p in metodo.Parameters) foreach (var x in Firma(p.ParameterType)) yield return x;
            if (!metodo.HasBody) continue;

            foreach (var v in metodo.Body.Variables) foreach (var x in Cuerpo(v.VariableType)) yield return x;
            foreach (var h in metodo.Body.ExceptionHandlers) foreach (var x in Cuerpo(h.CatchType)) yield return x;
            foreach (var instruccion in metodo.Body.Instructions)
                switch (instruccion.Operand)
                {
                    case MethodReference llamado:
                        foreach (var x in Cuerpo(llamado.DeclaringType)) yield return x;
                        foreach (var x in Cuerpo(llamado.ReturnType)) yield return x;
                        foreach (var p in llamado.Parameters) foreach (var x in Cuerpo(p.ParameterType)) yield return x;
                        if (llamado is GenericInstanceMethod generico)
                            foreach (var a in generico.GenericArguments) foreach (var x in Cuerpo(a)) yield return x;
                        break;
                    case FieldReference campo:
                        foreach (var x in Cuerpo(campo.DeclaringType)) yield return x;
                        foreach (var x in Cuerpo(campo.FieldType)) yield return x;
                        break;
                    case TypeReference referido:
                        foreach (var x in Cuerpo(referido)) yield return x;
                        break;
                }
        }
    }

    private static IEnumerable<(string Lugar, MemberReference Miembro)> MiembrosReferidos(TypeDefinition tipo)
    {
        foreach (var metodo in tipo.Methods.Where(m => m.HasBody))
            foreach (var instruccion in metodo.Body.Instructions)
                if (instruccion.Operand is MemberReference miembro and (MethodReference or FieldReference))
                    yield return ("cuerpo", miembro);
    }
}
