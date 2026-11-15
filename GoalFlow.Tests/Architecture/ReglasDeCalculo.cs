using Mono.Cecil;

namespace GoalFlow.Tests.Architecture;

/// <summary>
/// Spec 009 R-32 / T-18: los tipos de <c>GoalFlow.Application.Estadisticas.Calculo</c> no referencian, ni en sus firmas ni en el IL
/// de sus métodos, ningún tipo de <c>System.Text.RegularExpressions</c>, <c>System.IO</c>, <c>System.Net</c>,
/// <c>System.Threading.Tasks</c>, <c>System.Globalization</c> o <c>Microsoft.*</c>; ninguno de <c>System.DateTime</c>,
/// <c>System.DateTimeOffset</c>, <c>System.TimeProvider</c>, <c>System.Random</c> o <c>System.Environment</c>; y ningún namespace de
/// GoalFlow salvo el propio y <c>GoalFlow.Application.Torneos.Contracts</c>. Las pruebas reales y el canario invocan exactamente
/// <see cref="Analizar"/>.
/// </summary>
public static class ReglasDeCalculo
{
    public const string EspacioVerificado = "GoalFlow.Application.Estadisticas.Calculo";

    private static readonly string[] EspaciosProhibidos =
        ["System.Text.RegularExpressions", "System.IO", "System.Net", "System.Threading.Tasks", "System.Globalization", "Microsoft"];

    private static readonly string[] TiposProhibidos =
        ["System.DateTime", "System.DateTimeOffset", "System.TimeProvider", "System.Random", "System.Environment"];

    private static readonly string[] PermitidosDeGoalFlow = [EspacioVerificado, "GoalFlow.Application.Torneos.Contracts"];

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

        var externo = referido;
        while (externo.DeclaringType is not null) externo = externo.DeclaringType;
        if (EstaProhibido(externo)) violaciones.Add($"{desde.FullName} · {lugar} · {referido.FullName}");
    }

    private static bool EstaProhibido(TypeReference externo)
    {
        var espacio = externo.Namespace;
        if (EspaciosProhibidos.Any(p => EnEspacio(espacio, p))) return true;
        if (TiposProhibidos.Contains(externo.FullName, StringComparer.Ordinal)) return true;
        return EnEspacio(espacio, "GoalFlow") && !PermitidosDeGoalFlow.Any(p => EnEspacio(espacio, p));
    }

    private static bool EnEspacio(string espacio, string raiz) =>
        espacio == raiz || espacio.StartsWith(raiz + ".", StringComparison.Ordinal);
}
