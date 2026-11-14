using System.Diagnostics;
using System.Globalization;
using GoalFlow.Application.IA;
using GoalFlow.Application.IA.Verificacion;
using GoalFlow.Application.Torneos.Contracts;

namespace GoalFlow.Tests.IA.Verificacion;

/// <summary>Spec 008 T-01, T-03 y T-05 a T-09: el verificador determinista contra el corpus y contra casos puntuales.</summary>
public class VerificadorDeResumenTests
{
    private static string[] Reglas(string texto, EntradaCanonica entrada) => [.. VerificadorDeResumen.Verificar(texto, entrada).Reglas];

    private static string Veredicto(string[] reglas) => reglas.Length == 0 ? "aprobado" : "rechazado " + string.Join(",", reglas);

    // ---- T-01: los 14 textos reales ----

    [Theory]
    [MemberData(nameof(CorpusV1.IdsDeLosTextosReales), MemberType = typeof(CorpusV1))]
    public void T01_cada_texto_real_del_corpus_tiene_exactamente_el_veredicto_del_Apendice_A(string id)
    {
        var caso = CorpusV1.Caso(id);

        var resultado = VerificadorDeResumen.Verificar(caso.Texto, CorpusV1.Entrada(caso.Fixture));

        Assert.Equal(Veredicto(caso.ReglasEsperadas), Veredicto([.. resultado.Reglas]));
        Assert.Equal(caso.AprobadoEsperado, resultado.Aprobado);
    }

    [Fact]
    public void T01_el_corpus_tiene_14_textos_reales_y_la_version_del_verificador_es_v1()
    {
        Assert.Equal(14, CorpusV1.Reales.Count);
        Assert.Equal(14, CorpusV1.Reales.Select(c => c.Id).Distinct().Count());
        Assert.Equal("v1", VerificadorDeResumen.VersionDelVerificador);
    }

    // ---- T-03: las 12 mutaciones adversariales, con los dos falsos negativos fijados ----

    [Theory]
    [MemberData(nameof(CorpusV1.IdsDeLasMutaciones), MemberType = typeof(CorpusV1))]
    public void T03_cada_mutacion_tiene_exactamente_el_veredicto_del_Apendice_A(string id)
    {
        var caso = CorpusV1.Caso(id);

        var resultado = VerificadorDeResumen.Verificar(caso.Texto, CorpusV1.Entrada(caso.Fixture));

        Assert.Equal(Veredicto(caso.ReglasEsperadas), Veredicto([.. resultado.Reglas]));
    }

    [Fact]
    public void T03_a4_y_a8_son_los_dos_falsos_negativos_fijados_y_se_aprueban()
    {
        var fijados = CorpusV1.Mutaciones.Where(c => c.FalsoNegativoFijado).ToList();

        Assert.Equal(["a4", "a8"], fijados.Select(c => c.Id).ToArray());
        Assert.All(fijados, c =>
        {
            Assert.NotEqual(Etiqueta.Fiel, c.Etiqueta);
            Assert.True(VerificadorDeResumen.Verificar(c.Texto, CorpusV1.Entrada(c.Fixture)).Aprobado, c.Id);
        });
        Assert.Equal(12, CorpusV1.Mutaciones.Count);
    }

    // ---- T-05: el enmascarado va antes de los números y no distingue mayúsculas ----

    private static DatosDePartidoParaResumen PlanillaDeT05() => DatosDePrueba.Vacio() with
    {
        Fase = 2,
        Jornada = 4,
        Marcador = new MarcadorDelPartido(2, 0),
        Goles = [new GolDelPartido(LadoDelEquipo.Local, "Jugador Uno", "J-1", 2)],
        TarjetasInformadas = true,
        Tarjetas = [new TarjetaDelPartido(LadoDelEquipo.Visitante, "Jugador Cinco", "J-5", CodigosDeTorneos.Amarilla, 2)],
    };

    private const string TextoDeT05 = "Equipo Uno venció 2-0 a Equipo Dos con dos goles de Jugador Uno, y Jugador Cinco recibió 2 amarillas.";

    [Fact]
    public void T05_los_nombres_se_enmascaran_antes_de_leer_numeros_y_sin_distinguir_mayusculas()
    {
        var entrada = EntradaCanonica.Construir(PlanillaDeT05());

        Assert.Empty(Reglas(TextoDeT05, entrada));
        Assert.Empty(Reglas(TextoDeT05.Replace("Jugador Uno", "jugador uno").Replace("Jugador Cinco", "jugador cinco"), entrada));
        Assert.Equal(["V2"], Reglas(TextoDeT05 + " Fue uno de los partidos de la jornada 4.", entrada));
    }

    [Fact]
    public void T05_un_nombre_con_digitos_o_con_una_palabra_numerica_no_produce_numeros()
    {
        var datos = PlanillaDeT05() with
        {
            Torneo = "Copa 2026",
            Visitante = "Equipo Diez",
        };
        var entrada = EntradaCanonica.Construir(datos);

        Assert.Empty(Reglas("En la copa 2026, Equipo Uno venció 2-0 a Equipo Diez.", entrada));
        // Solo se enmascara la palabra completa: "Diezmo" no es "Equipo Diez" ni un nombre.
        Assert.Contains("V1", Reglas("Equipo Uno venció 2-0 a Diezmo.", entrada));
    }

    // ---- T-06: V6 sobre tarjetasInformadas ----

    public static IEnumerable<object[]> CasosDeT06() =>
    [
        // (informadas, con tarjetas, texto, ¿V6?)
        [true, false, "No se informaron tarjetas.", true],
        [true, false, "No hubo tarjetas.", false],
        [true, true, "No se informaron tarjetas.", true],
        [true, true, "No hubo tarjetas.", true],
        [false, false, "No se informaron tarjetas.", false],
        [false, false, "No hubo tarjetas.", true],
        [null!, false, "No se informaron tarjetas.", false],
        [null!, false, "No hubo tarjetas.", true],
    ];

    [Theory]
    [MemberData(nameof(CasosDeT06))]
    public void T06_V6_segun_tarjetasInformadas_y_el_texto(bool? informadas, bool conTarjetas, string texto, bool falla)
    {
        var datos = DatosDePrueba.Vacio() with
        {
            TarjetasInformadas = informadas,
            Tarjetas = conTarjetas
                ? [new TarjetaDelPartido(LadoDelEquipo.Visitante, "Jugador Tres", "J-3", CodigosDeTorneos.Amarilla, 1)]
                : [],
        };

        var reglas = Reglas(texto, EntradaCanonica.Construir(datos));

        Assert.Equal(falla ? ["V6"] : [], reglas);
    }

    // ---- T-07: V7, colores ----

    [Fact]
    public void T07_un_color_que_la_entrada_no_tiene_falla_V7_y_uno_que_tiene_pasa()
    {
        var conAmarilla = EntradaCanonica.Construir(DatosDePrueba.Vacio() with
        {
            Tarjetas = [new TarjetaDelPartido(LadoDelEquipo.Visitante, "Jugador Tres", "J-3", CodigosDeTorneos.Amarilla, 1)],
        });
        var sinTarjetas = EntradaCanonica.Construir(DatosDePrueba.Vacio());

        Assert.Equal(["V7"], Reglas("Hubo una roja.", conAmarilla));
        Assert.Empty(Reglas("Hubo amarillas.", conAmarilla));
        Assert.Equal(["V7"], Reglas("Hubo azules.", sinTarjetas));
    }

    // ---- T-08: V1, nombres ----

    [Fact]
    public void T08_V1_una_mayuscula_a_mitad_de_oracion_falla_y_el_inicio_solo_pasa_si_esta_en_la_lista()
    {
        var entrada = EntradaCanonica.Construir(DatosDePrueba.Vacio());

        Assert.Equal(["V1"], Reglas("En el partido Pérez anotó.", entrada));
        Assert.Equal(["V1"], Reglas("Pérez anotó un gol.", entrada));
        Assert.Empty(Reglas("Además hubo un gol.", entrada));
    }

    [Fact]
    public void T08_V1_el_valor_de_estado_y_W_O_enmascarados_pasan()
    {
        var entrada = EntradaCanonica.Construir(DatosDePrueba.Vacio());

        Assert.Empty(Reglas("El partido quedó con estado Jugado.", entrada));
        Assert.Empty(Reglas("Se declaró W.O. en el partido.", entrada));
    }

    // ---- T-09: determinismo, cultura y límites ----

    [Fact]
    public void T09_el_resultado_es_el_mismo_dos_veces_y_bajo_tr_TR()
    {
        var casos = CorpusV1.Reales.Concat(CorpusV1.Mutaciones).ToList();
        string[] Todos() => [.. casos.Select(c => c.Id + ":" + Veredicto(Reglas(c.Texto, CorpusV1.Entrada(c.Fixture))))];

        var primero = Todos();
        var segundo = Todos();
        var anterior = CultureInfo.CurrentCulture;
        string[] turco;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            turco = Todos();
            // Una "I" mayúscula: bajo tr-TR, un ToLower() de la cultura actual la volvería "ı" y el nombre ya no se enmascararía.
            var entrada = EntradaCanonica.Construir(DatosDePrueba.Vacio() with { Local = "Equipo Iris" });
            Assert.Empty(Reglas("EQUIPO IRIS 0-0 EQUIPO DOS.", entrada));
        }
        finally
        {
            CultureInfo.CurrentCulture = anterior;
        }

        Assert.Equal(primero, segundo);
        Assert.Equal(primero, turco);
    }

    [Fact]
    public void T09_un_texto_de_900_caracteres_de_mayusculas_y_digitos_con_200_nombres_termina_y_falla_V1_y_V2()
    {
        var datos = DatosDePrueba.Vacio() with
        {
            Goles = [.. Enumerable.Range(0, 200).Select(i => new GolDelPartido(LadoDelEquipo.Local, $"Jugador {i:000}", $"J-{i}", 1))],
        };
        var entrada = EntradaCanonica.Construir(datos);
        var texto = string.Concat(Enumerable.Repeat("Zeta 777 ", 100))[..900];
        Assert.Equal(900, texto.Length);

        var reloj = Stopwatch.StartNew();
        var reglas = Reglas(texto, entrada);
        reloj.Stop();

        Assert.Equal(["V1", "V2"], reglas);
        Assert.True(reloj.Elapsed < TimeSpan.FromSeconds(10), $"Tardó {reloj.Elapsed}");
    }

    // ---- R-01 y R-02: forma del resultado y enmascarado ----

    [Fact]
    public void R01_las_reglas_van_sin_repetir_y_en_el_orden_V1_a_V8()
    {
        var entrada = EntradaCanonica.Construir(DatosDePrueba.Vacio());

        // V7 (roja) y V3 (minuto) y V1 (Pérez), escritos en otro orden.
        var reglas = Reglas("Hubo una roja y otra roja. Fue en el minuto 90, dijo Pérez.", entrada);

        Assert.Equal(["V1", "V2", "V3", "V7"], reglas);
    }

    [Fact]
    public void R02_un_valor_solo_se_enmascara_entre_caracteres_que_no_son_letra_ni_digito()
    {
        var entrada = EntradaCanonica.Construir(DatosDePrueba.Vacio());

        // "Equipo Unos" contiene "Equipo Uno" pero continúa con una letra: no se enmascara, y "Unos" falla V1.
        Assert.Contains("V1", Reglas("El partido de Equipo Unos.", entrada));
        Assert.Empty(Reglas("El partido de (Equipo Uno).", entrada));
    }

    [Fact]
    public void R02_un_caracter_de_uso_privado_en_el_texto_no_se_hace_pasar_por_un_valor_enmascarado()
    {
        var datos = DatosDePrueba.Vacio() with
        {
            TarjetasInformadas = true,
            Tarjetas = [new TarjetaDelPartido(LadoDelEquipo.Visitante, "Jugador Tres", "J-3", CodigosDeTorneos.Amarilla, 1)],
        };
        var entrada = EntradaCanonica.Construir(datos);

        // Con un nombre real, la negación no se aplica; con el carácter de uso privado, sí debe aplicarse.
        Assert.Empty(Reglas("No hubo ninguna tarjeta de Jugador Tres.", entrada));
        Assert.Equal(["V6", "V8"], Reglas("No hubo ninguna tarjeta de \uE000.", entrada));
    }

    // ---- T-15 (R-02, R-11 b): NFC y caracteres invisibles. Los invisibles se escriben con escapes \u, nunca literales. ----

    [Theory]
    [InlineData("a_U+00AD_dentro_de_amarilla")]
    [InlineData("b_U+E000_entre_dos_palabras")]
    [InlineData("c_U+200B_dentro_de_descontó")]
    [InlineData("d_C1_entero_en_NFD")]
    [InlineData("e_nombre_guardado_descompuesto_y_escrito_compuesto")]
    [InlineData("f_nombre_de_equipo_con_U+200D_copiado_tal_cual")]
    public void T15_NFC_y_caracteres_invisibles(string caso)
    {
        var c1 = CorpusV1.Caso("C1");
        var entrada = CorpusV1.Entrada("F1");
        string texto;
        string[] esperado;

        switch (caso[0])
        {
            case 'a':
                texto = Reemplazar(c1.Texto, "amarilla", "ama\u00ADrilla");
                esperado = ["V8"];
                break;
            case 'b':
                texto = Reemplazar(c1.Texto, "El partido fue", "El partido \uE000 fue");
                esperado = ["V8"];
                break;
            case 'c':
                texto = Reemplazar(CorpusV1.Caso("M6").Texto, "descontó", "des\u200Bcontó");
                esperado = ["V4", "V8"];
                break;
            case 'd':
                texto = c1.Texto.Normalize(System.Text.NormalizationForm.FormD);
                Assert.NotEqual(c1.Texto, texto); // el caso es real: hay letras acentuadas descompuestas
                esperado = [];
                break;
            case 'e':
            {
                var datos = DatosDePrueba.Vacio() with
                {
                    Marcador = new MarcadorDelPartido(1, 0),
                    Goles = [new GolDelPartido(LadoDelEquipo.Local, "Pe\u0301rez", "J-1", 1)],
                };
                entrada = EntradaCanonica.Construir(datos);
                texto = "Equipo Uno venció 1-0 a Equipo Dos con gol de Pérez.";
                esperado = [];
                break;
            }

            default: // f
            {
                var local = "Equipo U\u200Dno";
                entrada = EntradaCanonica.Construir(CorpusV1.Datos("F1") with { Local = local });
                texto = Reemplazar(c1.Texto, "Equipo Uno", local);
                esperado = [];
                break;
            }
        }

        Assert.Equal(esperado, Reglas(texto, entrada));
    }

    // El fragmento debe aparecer exactamente una vez en el texto base.
    private static string Reemplazar(string texto, string de, string a)
    {
        var primero = texto.IndexOf(de, StringComparison.Ordinal);
        Assert.True(primero >= 0 && texto.IndexOf(de, primero + 1, StringComparison.Ordinal) < 0, $"'{de}' debe aparecer una vez");
        return texto.Replace(de, a, StringComparison.Ordinal);
    }

    // ---- T-16 (R-04): números que en A solo existen como suma (Spec 008 C-16) ----

    [Theory]
    [InlineData("a_F3_seis_es_3_mas_3", "F3", "En la jornada 3 de la fase 1 del Torneo Uno, Equipo Uno y Equipo Dos igualaron 3 a 3 en un partido de seis goles.")]
    [InlineData("b_F5_cuatro_es_2_mas_1_mas_1", "F5", "En la jornada 3 de la fase 1 del Torneo Uno, Equipo Uno y Equipo Dos igualaron 0 a 0, con cuatro tarjetas informadas en total.")]
    public void T16_un_numero_que_en_A_solo_existe_como_suma_se_aprueba(string caso, string fixture, string texto)
    {
        Assert.NotEmpty(caso);

        var resultado = VerificadorDeResumen.Verificar(texto, CorpusV1.Entrada(fixture));

        Assert.True(resultado.Aprobado, $"{caso}: reglas {string.Join(",", resultado.Reglas)}");
    }
}
