using Mono.Cecil;

namespace GoalFlow.Tests.Architecture;

/// <summary>
/// Spec 008 R-21 / T-14: los tipos de <c>GoalFlow.Application.IA.Verificacion</c> no referencian, ni en sus firmas ni en el IL de
/// sus métodos, ningún tipo de <c>System.Text.RegularExpressions</c>, <c>System.IO</c>, <c>System.Net</c>,
/// <c>System.Threading.Tasks</c> o <c>Microsoft.*</c>, ni ningún namespace de GoalFlow salvo <c>GoalFlow.Application.IA</c>
/// (y sus sub-namespaces) y <c>GoalFlow.Application.Torneos.Contracts</c>. Las pruebas reales y el canario invocan exactamente <see cref="Analizar"/>.
/// </summary>
public static class ReglasDeVerificacion
{
    public const string EspacioVerificado = "GoalFlow.Application.IA.Verificacion";

    private static readonly string[] Prohibidos =
        ["System.Text.RegularExpressions", "System.IO", "System.Net", "System.Threading.Tasks", "Microsoft"];

    private static readonly string[] PermitidosDeGoalFlow = ["GoalFlow.Application.IA", "GoalFlow.Application.Torneos.Contracts"];

    /// <summary>Cada violación dice el tipo que la comete, si está en una firma o en un cuerpo, y el tipo referido.</summary>
    public static ResultadoRegla Analizar(IEnumerable<TypeDefinition> tipos)
    {
        var violaciones = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var tipo in tipos)
        {
            void Firma(TypeReference? referido) => Revisar(referido, tipo, "firma", violaciones);
            void Cuerpo(TypeReference? referido) => Revisar(referido, tipo, "cuerpo", violaciones);

            Firma(tipo.BaseType);
            foreach (var i in tipo.Interfaces) Firma(i.InterfaceType);
            foreach (var a in tipo.CustomAttributes) Firma(a.AttributeType);
            foreach (var f in tipo.Fields) Firma(f.FieldType);
            foreach (var p in tipo.Properties) Firma(p.PropertyType);
            foreach (var e in tipo.Events) Firma(e.EventType);

            foreach (var metodo in tipo.Methods)
            {
                Firma(metodo.ReturnType);
                foreach (var p in metodo.Parameters) Firma(p.ParameterType);
                foreach (var a in metodo.CustomAttributes) Firma(a.AttributeType);
                if (!metodo.HasBody) continue;

                foreach (var variable in metodo.Body.Variables) Cuerpo(variable.VariableType);
                foreach (var manejador in metodo.Body.ExceptionHandlers) Cuerpo(manejador.CatchType);
                foreach (var instruccion in metodo.Body.Instructions)
                {
                    switch (instruccion.Operand)
                    {
                        case MethodReference llamado:
                            Cuerpo(llamado.DeclaringType);
                            Cuerpo(llamado.ReturnType);
                            foreach (var p in llamado.Parameters) Cuerpo(p.ParameterType);
                            if (llamado is GenericInstanceMethod generico)
                                foreach (var argumento in generico.GenericArguments) Cuerpo(argumento);
                            break;
                        case FieldReference campo:
                            Cuerpo(campo.DeclaringType);
                            Cuerpo(campo.FieldType);
                            break;
                        case TypeReference referido:
                            Cuerpo(referido);
                            break;
                    }
                }
            }
        }

        return ResultadoRegla.Desde([.. violaciones]);
    }

    private static void Revisar(TypeReference? referido, TypeDefinition desde, string lugar, ISet<string> violaciones)
    {
        switch (referido)
        {
            case null or GenericParameter:
                return;
            case GenericInstanceType generico:
                Revisar(generico.ElementType, desde, lugar, violaciones);
                foreach (var argumento in generico.GenericArguments) Revisar(argumento, desde, lugar, violaciones);
                return;
            case TypeSpecification especificacion:
                Revisar(especificacion.ElementType, desde, lugar, violaciones);
                return;
        }

        var espacio = EspacioExterno(referido);
        if (EstaProhibido(espacio)) violaciones.Add($"{desde.FullName} · {lugar} · {referido.FullName}");
    }

    private static string EspacioExterno(TypeReference tipo)
    {
        while (tipo.DeclaringType is not null) tipo = tipo.DeclaringType;
        return tipo.Namespace;
    }

    private static bool EstaProhibido(string espacio)
    {
        if (Prohibidos.Any(p => EnEspacio(espacio, p))) return true;
        return EnEspacio(espacio, "GoalFlow") && !PermitidosDeGoalFlow.Any(p => EnEspacio(espacio, p));
    }

    private static bool EnEspacio(string espacio, string raiz) =>
        espacio == raiz || espacio.StartsWith(raiz + ".", StringComparison.Ordinal);
}
