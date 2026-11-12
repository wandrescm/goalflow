namespace GoalFlow.Application.IA;

/// <summary>Cómo razona el modelo (ADR-12 §2.7): con <c>Deshabilitado</c> se envía <c>thinking: {"type": "disabled"}</c>.</summary>
public enum RazonamientoDeIA
{
    Deshabilitado,
    Adaptativo,
}

public sealed class OpcionesDeBackfill
{
    /// <summary>El backfill corre una vez por arranque solo si esto y <c>IA:Habilitada</c> son true (Spec 007 R-32).</summary>
    public bool EjecutarAlArrancar { get; set; }
}

/// <summary>
/// Sección <c>IA</c> (Spec 007 R-06, ADR-12 §2.10). Montos en micro-dólares enteros; precios en µ$ por millón de tokens.
/// <see cref="Habilitada"/> es false por defecto en todo ambiente. <see cref="ClaveApi"/> viene de una variable de entorno
/// (<c>IA__ClaveApi</c>), nunca del repositorio, y nunca se registra en un log.
/// </summary>
public sealed class OpcionesDeIA
{
    public const string Seccion = "IA";

    public bool Habilitada { get; set; }

    public string? ClaveApi { get; set; }

    public string Modelo { get; set; } = "claude-haiku-5-5";

    public long PrecioEntradaMicroUsdPorMTok { get; set; } = 100_000;

    public long PrecioSalidaMicroUsdPorMTok { get; set; } = 500_000;

    public long TopeMensualPorLigaMicroUsd { get; set; } = 1_500_000;

    public long TopeMensualGlobalMicroUsd { get; set; } = 3_000_000;

    public int MaximoDeLlamadasPorHuella { get; set; } = 3;

    public int MaxTokensDeSalida { get; set; } = 600;

    /// <summary>Tamaño máximo de la entrada canónica, antes de contarla.</summary>
    public int MaximoDeBytesDeEntrada { get; set; } = 16_384;

    /// <summary>Máximo de tokens contados de la solicitud: por encima nunca se envía.</summary>
    public int MaximoDeTokensDeEntrada { get; set; } = 8_000;

    public int MargenDeEntradaPorcentaje { get; set; } = 10;

    public int MargenDeEntradaTokens { get; set; } = 50;

    public RazonamientoDeIA Razonamiento { get; set; } = RazonamientoDeIA.Deshabilitado;

    public int LongitudMaximaDeBorrador { get; set; } = 4_000;

    public TimeSpan TimeoutDeLlamada { get; set; } = TimeSpan.FromSeconds(60);

    public OpcionesDeBackfill Backfill { get; set; } = new();

    public PreciosDeIa Precios => new(
        PrecioEntradaMicroUsdPorMTok, PrecioSalidaMicroUsdPorMTok, MargenDeEntradaPorcentaje, MargenDeEntradaTokens, MaxTokensDeSalida);
}
