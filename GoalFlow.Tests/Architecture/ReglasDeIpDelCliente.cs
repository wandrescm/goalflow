using Mono.Cecil;

namespace GoalFlow.Tests.Architecture;

/// <summary>
/// Spec 010 R-22 / T-21: en el código de producción, (a) el literal <c>CF-Connecting-IP</c> solo aparece en
/// <see cref="TipoDeLaParticion"/> (y sus tipos anidados o generados por el compilador) y (b) nadie llama a
/// <c>ConnectionInfo.get_RemoteIpAddress</c>. Las pruebas reales y los canarios invocan exactamente <see cref="Analizar"/>.
/// <para>Control estático con límites conocidos: no ve una lectura por reflexión, código dinámico ni un nombre de encabezado construido
/// en tiempo de ejecución; los dos canarios y la revisión de código son sus controles complementarios.</para>
/// </summary>
public static class ReglasDeIpDelCliente
{
    public const string TipoDeLaParticion = "GoalFlow.Api.Hosting.ParticionDeFrecuencia";

    private const string Encabezado = "CF-Connecting-IP";
    private const string TipoDeConexion = "Microsoft.AspNetCore.Http.ConnectionInfo";
    private const string LectorDeLaIp = "get_RemoteIpAddress";

    public static ResultadoRegla Analizar(IEnumerable<TypeDefinition> tipos)
    {
        var violaciones = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var tipo in tipos)
        {
            var externo = ReglasDeConsultas.TipoExterno(tipo);

            foreach (var campo in tipo.Fields)
                if (EsElEncabezado(campo.Constant) && externo.FullName != TipoDeLaParticion)
                    violaciones.Add($"{tipo.FullName}.{campo.Name} · (a) constante {Encabezado}");

            foreach (var metodo in tipo.Methods.Where(m => m.HasBody))
                foreach (var instruccion in metodo.Body.Instructions)
                {
                    if (EsElEncabezado(instruccion.Operand) && externo.FullName != TipoDeLaParticion)
                        violaciones.Add($"{tipo.FullName}.{metodo.Name} · (a) literal {Encabezado}");

                    if (instruccion.Operand is MethodReference llamado && llamado.DeclaringType.FullName == TipoDeConexion && llamado.Name == LectorDeLaIp)
                        violaciones.Add($"{tipo.FullName}.{metodo.Name} · (b) {TipoDeConexion}::{LectorDeLaIp}");
                }
        }

        return ResultadoRegla.Desde([.. violaciones]);
    }

    /// <summary>Los tipos externos (por nombre completo) cuyo IL o constantes contienen el literal del encabezado, sin filtrar.</summary>
    public static IReadOnlyList<string> TiposQueNombranElEncabezado(IEnumerable<TypeDefinition> tipos)
    {
        var nombres = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var tipo in tipos)
        {
            var conLiteral = tipo.Fields.Any(c => EsElEncabezado(c.Constant))
                || tipo.Methods.Where(m => m.HasBody).Any(m => m.Body.Instructions.Any(i => EsElEncabezado(i.Operand)));
            if (conLiteral) nombres.Add(ReglasDeConsultas.TipoExterno(tipo).FullName);
        }

        return [.. nombres];
    }

    private static bool EsElEncabezado(object? valor) =>
        valor is string texto && string.Equals(texto, Encabezado, StringComparison.OrdinalIgnoreCase);
}
