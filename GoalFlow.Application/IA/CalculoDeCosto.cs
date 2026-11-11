namespace GoalFlow.Application.IA;

/// <summary>Precios en µ$ por millón de tokens y los márgenes de la reserva (Spec 007 R-12, ADR-12 §2.10).</summary>
public sealed record PreciosDeIa(
    long PrecioEntradaMicroUsdPorMTok,
    long PrecioSalidaMicroUsdPorMTok,
    int MargenDeEntradaPorcentaje,
    int MargenDeEntradaTokens,
    int MaxTokensDeSalida);

/// <summary>
/// El monto de la reserva y el costo real, en micro-dólares enteros redondeados hacia arriba, cada parte por separado
/// (Spec 007 R-12). La parte de salida es una cota contractual (<c>max_tokens</c>); la de entrada es una estimación y nunca
/// se supone cota (R-23, ADR-12 C-20).
/// </summary>
public static class CalculoDeCosto
{
    private const long TokensPorMega = 1_000_000;

    /// <summary>⌈(N × (100 + margen%) / 100 + margenTokens) × precioEntrada / 10⁶⌉ + ⌈maxTokensSalida × precioSalida / 10⁶⌉.</summary>
    public static long Reserva(int tokensContados, PreciosDeIa p)
    {
        checked
        {
            // Entrada en centésimas de token: N × (100 + margen%) + 100 × margenTokens.
            var centesimas = (long)tokensContados * (100 + p.MargenDeEntradaPorcentaje) + 100L * p.MargenDeEntradaTokens;
            var entrada = DivisionHaciaArriba(centesimas * p.PrecioEntradaMicroUsdPorMTok, 100 * TokensPorMega);
            var salida = DivisionHaciaArriba(p.MaxTokensDeSalida * p.PrecioSalidaMicroUsdPorMTok, TokensPorMega);
            return entrada + salida;
        }
    }

    /// <summary>El costo con el uso reportado, con los mismos precios y el mismo redondeo.</summary>
    public static long CostoReal(int tokensEntrada, int tokensSalida, PreciosDeIa p)
    {
        checked
        {
            return DivisionHaciaArriba(tokensEntrada * p.PrecioEntradaMicroUsdPorMTok, TokensPorMega)
                + DivisionHaciaArriba(tokensSalida * p.PrecioSalidaMicroUsdPorMTok, TokensPorMega);
        }
    }

    private static long DivisionHaciaArriba(long numerador, long denominador) => (numerador + denominador - 1) / denominador;
}
