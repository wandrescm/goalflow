using GoalFlow.Application.IA;

namespace GoalFlow.Tests.IA;

/// <summary>Spec 007 T-05 (R-12) y T-20 (R-24): la fórmula de la reserva y la validación de la salida del modelo.</summary>
public class CostoYSalidaTests
{
    // Valores iniciales de ADR-12 §2.10.
    private static readonly PreciosDeIa Iniciales = new(
        PrecioEntradaMicroUsdPorMTok: 100_000,
        PrecioSalidaMicroUsdPorMTok: 500_000,
        MargenDeEntradaPorcentaje: 10,
        MargenDeEntradaTokens: 50,
        MaxTokensDeSalida: 600);

    [Theory] // T-05
    [InlineData(2_000, 525)]
    [InlineData(8_000, 1_185)]
    [InlineData(1, 306)] // el redondeo hacia arriba de la parte de entrada
    public void T05_la_reserva_con_los_valores_iniciales(int tokensContados, long esperada)
    {
        Assert.Equal(esperada, CalculoDeCosto.Reserva(tokensContados, Iniciales));
    }

    [Fact]
    public void T05_cada_parte_se_redondea_hacia_arriba_por_separado()
    {
        // Entrada: (N*110/100 + 50) tokens * 100_000 / 1_000_000. Con N = 5: 55.5 tokens → 5.55 µ$ → 6; salida 300.
        Assert.Equal(306, CalculoDeCosto.Reserva(5, Iniciales));
        // Sin margen y con precios que dejan fracciones en ambas partes: 1*3/1e6 → 1 y 1*3/1e6 → 1.
        var chico = new PreciosDeIa(3, 3, 0, 0, 1);
        Assert.Equal(2, CalculoDeCosto.Reserva(1, chico));
    }

    [Fact]
    public void T05_el_costo_real_redondea_hacia_arriba_cada_parte_con_los_mismos_precios()
    {
        // 2000 entrada → 200_000_000/1e6 = 200; 400 salida → 200_000_000/1e6 = 200.
        Assert.Equal(400, CalculoDeCosto.CostoReal(2_000, 400, Iniciales));
        Assert.Equal(2, CalculoDeCosto.CostoReal(1, 1, Iniciales)); // 0.1 → 1 y 0.5 → 1
        Assert.Equal(0, CalculoDeCosto.CostoReal(0, 0, Iniciales));
    }

    [Fact]
    public void T05_el_margen_porcentual_y_el_de_tokens_suben_la_reserva()
    {
        var sinMargen = Iniciales with { MargenDeEntradaPorcentaje = 0, MargenDeEntradaTokens = 0 };
        var conMargen = Iniciales with { MargenDeEntradaPorcentaje = 50, MargenDeEntradaTokens = 1_000 };

        Assert.Equal(500, CalculoDeCosto.Reserva(2_000, sinMargen)); // 200 + 300
        Assert.Equal(300 + 400, CalculoDeCosto.Reserva(2_000, conMargen)); // (3000+1000)*0.1 = 400
    }

    [Theory] // T-20
    [InlineData("miembro extra", false)]
    [InlineData("sin texto", false)]
    [InlineData("79 caracteres", false)]
    [InlineData("80 caracteres", true)]
    [InlineData("900 caracteres", true)]
    [InlineData("901 caracteres", false)]
    [InlineData("salto de línea", false)]
    [InlineData("válida", true)]
    public void T20_la_validacion_de_la_salida(string caso, bool esValida)
    {
        string Json(string texto) => System.Text.Json.JsonSerializer.Serialize(new { texto });
        var respuesta = caso switch
        {
            "miembro extra" => """{"texto":"%","otro":1}""".Replace("%", new string('a', 100)),
            "sin texto" => """{"resumen":"hola"}""",
            "79 caracteres" => Json(new string('a', 79)),
            "80 caracteres" => Json(new string('a', 80)),
            "900 caracteres" => Json(new string('a', 900)),
            "901 caracteres" => Json(new string('a', 901)),
            "salto de línea" => Json(new string('a', 50) + "\n" + new string('b', 50)),
            "válida" => Json("Equipo Uno venció 2-1 a Equipo Dos con goles de Jugador Uno y Jugador Dos en un partido muy disputado."),
            _ => throw new InvalidOperationException(caso),
        };

        var resultado = ValidadorDeSalidaDelModelo.Validar(respuesta);

        Assert.Equal(esValida, resultado.EsValida);
        Assert.Equal(esValida, resultado.Texto is not null);
    }

    [Fact]
    public void T20_los_espacios_de_los_extremos_se_recortan_antes_de_contar()
    {
        var respuesta = System.Text.Json.JsonSerializer.Serialize(new { texto = "  \n" + new string('a', 80) + "  " });

        var resultado = ValidadorDeSalidaDelModelo.Validar(respuesta);

        Assert.True(resultado.EsValida);
        Assert.Equal(new string('a', 80), resultado.Texto);
    }

    [Theory]
    [InlineData("no es JSON")]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("\"texto\"")]
    [InlineData("{\"texto\":42}")]
    [InlineData("{\"texto\":null}")]
    public void T20_una_salida_que_no_es_el_objeto_esperado_es_invalida_sin_lanzar(string respuesta)
    {
        Assert.False(ValidadorDeSalidaDelModelo.Validar(respuesta).EsValida);
    }
}
