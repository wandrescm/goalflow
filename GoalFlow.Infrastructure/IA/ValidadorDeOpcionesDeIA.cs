using GoalFlow.Application.IA;
using GoalFlow.Infrastructure.Mensajeria;
using Microsoft.Extensions.Options;

namespace GoalFlow.Infrastructure.IA;

/// <summary>
/// Spec 007 R-06: con <c>IA:Habilitada</c> en true, el host no arranca con una configuración que no puede cumplirse. Solo se registra
/// cuando la IA está habilitada: apagada, ninguna opción de IA se valida. Nunca incluye un valor en el mensaje (ni la clave).
/// </summary>
public sealed class ValidadorDeOpcionesDeIA(IOptions<OpcionesDeMensajeria> mensajeria) : IValidateOptions<OpcionesDeIA>
{
    public ValidateOptionsResult Validate(string? name, OpcionesDeIA o)
    {
        if (!o.Habilitada) return ValidateOptionsResult.Success;

        var errores = new List<string>();
        if (string.IsNullOrWhiteSpace(o.ClaveApi)) errores.Add($"{nameof(o.ClaveApi)} es obligatoria con la IA habilitada.");
        if (string.IsNullOrWhiteSpace(o.Modelo)) errores.Add($"{nameof(o.Modelo)} no puede estar vacío.");
        if (o.PrecioEntradaMicroUsdPorMTok <= 0) errores.Add($"{nameof(o.PrecioEntradaMicroUsdPorMTok)} debe ser positivo.");
        if (o.PrecioSalidaMicroUsdPorMTok <= 0) errores.Add($"{nameof(o.PrecioSalidaMicroUsdPorMTok)} debe ser positivo.");
        if (o.TopeMensualPorLigaMicroUsd <= 0) errores.Add($"{nameof(o.TopeMensualPorLigaMicroUsd)} debe ser positivo.");
        if (o.TopeMensualPorLigaMicroUsd > o.TopeMensualGlobalMicroUsd)
            errores.Add($"{nameof(o.TopeMensualPorLigaMicroUsd)} no puede superar {nameof(o.TopeMensualGlobalMicroUsd)}.");
        if (o.MaximoDeLlamadasPorHuella < 1) errores.Add($"{nameof(o.MaximoDeLlamadasPorHuella)} debe ser al menos 1.");
        if (o.MaxTokensDeSalida < 1) errores.Add($"{nameof(o.MaxTokensDeSalida)} debe ser al menos 1.");
        if (o.MaximoDeBytesDeEntrada is < 1 or > 65_536) errores.Add($"{nameof(o.MaximoDeBytesDeEntrada)} debe estar entre 1 y 65536.");
        if (o.MaximoDeTokensDeEntrada is < 1 or > 50_000) errores.Add($"{nameof(o.MaximoDeTokensDeEntrada)} debe estar entre 1 y 50000.");
        if (o.MargenDeEntradaPorcentaje < 0) errores.Add($"{nameof(o.MargenDeEntradaPorcentaje)} no puede ser negativo.");
        if (o.MargenDeEntradaTokens < 0) errores.Add($"{nameof(o.MargenDeEntradaTokens)} no puede ser negativo.");
        if (o.LongitudMaximaDeBorrador is < 400 or > 4_000) errores.Add($"{nameof(o.LongitudMaximaDeBorrador)} debe estar entre 400 y 4000.");
        if (!Enum.IsDefined(o.Razonamiento)) errores.Add($"{nameof(o.Razonamiento)} no es un valor conocido.");
        if (o.TimeoutDeLlamada <= TimeSpan.Zero || o.TimeoutDeLlamada >= mensajeria.Value.TimeoutDelConsumidor)
            errores.Add($"{nameof(o.TimeoutDeLlamada)} debe ser positivo y menor que Mensajeria:{nameof(OpcionesDeMensajeria.TimeoutDelConsumidor)}.");

        return errores.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errores);
    }
}
