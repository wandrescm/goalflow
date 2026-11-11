using System.Text;
using GoalFlow.Application.IA;
using GoalFlow.Application.Torneos.Contracts;

namespace GoalFlow.Tests.IA;

/// <summary>Spec 007 T-01 y T-02 (R-08, R-09): la entrada canónica y su huella. Código puro, sin E/S.</summary>
public class EntradaCanonicaTests
{
    private const string BytesDorados =
        """{"torneo":"Torneo Uno","fase":1,"jornada":3,"local":"Equipo Uno","visitante":"Equipo Dos","estado":"Jugado","marcador":{"local":1,"visitante":1},"oficial":null,"motivo":null,"goles":[{"equipo":"local","jugador":"Jugador Dos","cantidad":1}],"autogoles":[{"jugador":"Jugador Uno","equipoDelAutor":"local","equipoBeneficiado":"visitante","cantidad":1}],"golesSinAutor":{"local":0,"visitante":0},"tarjetasInformadas":true,"tarjetas":[{"equipo":"visitante","jugador":"Jugador Tres","color":"Amarilla","cantidad":1}]}""";

    private static string Huella(DatosDePartidoParaResumen datos) => EntradaCanonica.Construir(datos).Huella();

    [Theory] // T-01
    [InlineData("marcador")]
    [InlineData("goleador")]
    [InlineData("autogol")]
    [InlineData("sin autor")]
    [InlineData("color de tarjeta")]
    [InlineData("tarjetas informadas")]
    [InlineData("nombre de equipo")]
    [InlineData("motivo")]
    [InlineData("lados del autogol")]
    public void T01_cambiar_cada_valor_de_R08_cambia_la_huella_y_el_autogol_local_se_acredita_al_visitante(string caso)
    {
        var @base = DatosDePrueba.ConAutogol();

        if (caso == "lados del autogol")
        {
            var json = Encoding.UTF8.GetString(EntradaCanonica.Construir(@base).Bytes());
            Assert.Contains("\"equipoDelAutor\":\"local\",\"equipoBeneficiado\":\"visitante\"", json);

            var delVisitante = @base with { Autogoles = [new AutogolDelPartido("Jugador Cuatro", "JUG-004", LadoDelEquipo.Visitante, 1)] };
            Assert.Contains(
                "\"equipoDelAutor\":\"visitante\",\"equipoBeneficiado\":\"local\"",
                Encoding.UTF8.GetString(EntradaCanonica.Construir(delVisitante).Bytes()));
            return;
        }

        var cambiado = caso switch
        {
            "marcador" => @base with { Marcador = new MarcadorDelPartido(2, 1) },
            "goleador" => @base with { Goles = [new GolDelPartido(LadoDelEquipo.Local, "Jugador Cinco", "JUG-005", 1)] },
            "autogol" => @base with { Autogoles = [new AutogolDelPartido("Jugador Seis", "JUG-006", LadoDelEquipo.Local, 1)] },
            "sin autor" => @base with { GolesSinAutor = new MarcadorDelPartido(1, 0) },
            "color de tarjeta" => @base with { Tarjetas = [new TarjetaDelPartido(LadoDelEquipo.Visitante, "Jugador Tres", "JUG-003", CodigosDeTorneos.Roja, 1)] },
            "tarjetas informadas" => Vacio(informadas: false) with { },
            "nombre de equipo" => @base with { Visitante = "Equipo Tres" },
            "motivo" => @base with { Motivo = CodigosDeTorneos.Otro },
            _ => throw new InvalidOperationException(caso),
        };

        if (caso == "tarjetas informadas")
        {
            // false → true con cero tarjetas: la huella cambia aunque la lista de tarjetas no (ADR-12 C-04).
            Assert.NotEqual(Huella(Vacio(informadas: false)), Huella(Vacio(informadas: true)));
            return;
        }

        Assert.NotEqual(Huella(@base), Huella(cambiado));
    }

    private static DatosDePartidoParaResumen Vacio(bool informadas) => DatosDePrueba.Vacio() with { TarjetasInformadas = informadas };

    [Fact] // T-01: bytes dorados
    public void T01_los_bytes_canonicos_de_una_planilla_con_autogol_son_exactamente_los_dorados()
    {
        var bytes = EntradaCanonica.Construir(DatosDePrueba.ConAutogol()).Bytes();

        Assert.Equal(BytesDorados, Encoding.UTF8.GetString(bytes));
    }

    [Fact] // R-08: sin escapes de más
    public void T01_las_letras_no_ASCII_se_escriben_tal_cual_y_solo_se_escapa_lo_que_JSON_exige()
    {
        var datos = DatosDePrueba.Vacio() with { Local = "Peñarol \"A\"", Visitante = "Ñandú\\B" };

        var json = Encoding.UTF8.GetString(EntradaCanonica.Construir(datos).Bytes());

        Assert.Contains("\"local\":\"Peñarol \\\"A\\\"\"", json);
        Assert.Contains("\"visitante\":\"Ñandú\\\\B\"", json);
        Assert.DoesNotContain("\\u", json);
    }

    [Fact] // R-08: ninguna entrada filtra identificadores, códigos ni razones
    public void T01_la_entrada_no_contiene_identificadores_codigos_version_razon_ni_hora()
    {
        var datos = DatosDePrueba.ConAutogol() with { MotivoDeRectificacion = "ErrorDeRegistro" };

        var json = Encoding.UTF8.GetString(EntradaCanonica.Construir(datos).Bytes());

        Assert.DoesNotContain("JUG-", json);
        Assert.DoesNotContain(DatosDePrueba.TorneoUno.ToString(), json);
        Assert.DoesNotContain("ErrorDeRegistro", json);
        Assert.DoesNotContain("2026-11-14", json);
        Assert.DoesNotContain("version", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact] // R-09: la huella es el SHA-256 en minúsculas hexadecimales de los bytes
    public void T01_la_huella_es_el_SHA256_hexadecimal_en_minusculas_de_los_bytes()
    {
        var entrada = EntradaCanonica.Construir(DatosDePrueba.ConAutogol());

        var esperada = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(BytesDorados))).ToLowerInvariant();

        Assert.Equal(esperada, entrada.Huella());
        Assert.Matches("^[0-9a-f]{64}$", entrada.Huella());
    }

    [Fact] // R-08: las listas se ordenan solo por lo que emiten
    public void T01_las_listas_se_ordenan_por_lado_nombre_color_y_cantidad()
    {
        var datos = DatosDePrueba.Vacio() with
        {
            Goles =
            [
                new GolDelPartido(LadoDelEquipo.Visitante, "Ana", "Z-1", 1),
                new GolDelPartido(LadoDelEquipo.Local, "Zoe", "A-1", 1),
                new GolDelPartido(LadoDelEquipo.Local, "Ana", "A-2", 3),
                new GolDelPartido(LadoDelEquipo.Local, "Ana", "A-3", 1),
            ],
            Tarjetas =
            [
                new TarjetaDelPartido(LadoDelEquipo.Local, "Ana", "A-2", CodigosDeTorneos.Roja, 1),
                new TarjetaDelPartido(LadoDelEquipo.Local, "Ana", "A-2", CodigosDeTorneos.Amarilla, 2),
            ],
        };

        var json = Encoding.UTF8.GetString(EntradaCanonica.Construir(datos).Bytes());

        Assert.Contains(
            "\"goles\":[{\"equipo\":\"local\",\"jugador\":\"Ana\",\"cantidad\":1},{\"equipo\":\"local\",\"jugador\":\"Ana\",\"cantidad\":3},"
            + "{\"equipo\":\"local\",\"jugador\":\"Zoe\",\"cantidad\":1},{\"equipo\":\"visitante\",\"jugador\":\"Ana\",\"cantidad\":1}]",
            json);
        Assert.Contains("\"color\":\"Amarilla\",\"cantidad\":2},{\"equipo\":\"local\",\"jugador\":\"Ana\",\"color\":\"Roja\"", json);
    }

    [Theory] // T-02
    [InlineData("version")]
    [InlineData("motivo de rectificación")]
    [InlineData("hora programada")]
    [InlineData("homónimo con otra inscripción")]
    [InlineData("homónimos intercambiados")]
    public void T02_lo_que_no_se_emite_no_cambia_la_huella(string caso)
    {
        var @base = DatosDePrueba.Vacio() with
        {
            Marcador = new MarcadorDelPartido(3, 0),
            Goles =
            [
                new GolDelPartido(LadoDelEquipo.Local, "Jugador Uno", "JUG-001", 2),
                new GolDelPartido(LadoDelEquipo.Local, "Jugador Uno", "JUG-009", 1),
            ],
        };

        var mismo = caso switch
        {
            "version" => @base with { Version = 9 },
            "motivo de rectificación" => @base with { MotivoDeRectificacion = "PartidoEquivocado" },
            "hora programada" => @base with { ProgramadoPara = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
            // Un gol pasa de una inscripción a otra del mismo nombre y del mismo lado: la entrada es idéntica.
            "homónimo con otra inscripción" => @base with
            {
                Goles =
                [
                    new GolDelPartido(LadoDelEquipo.Local, "Jugador Uno", "JUG-777", 2),
                    new GolDelPartido(LadoDelEquipo.Local, "Jugador Uno", "JUG-009", 1),
                ],
            },
            // Dos homónimos con 2 y 1 goles, intercambiados entre sus inscripciones: los bytes no cambian (ADR-12 C-18).
            "homónimos intercambiados" => @base with
            {
                Goles =
                [
                    new GolDelPartido(LadoDelEquipo.Local, "Jugador Uno", "JUG-001", 1),
                    new GolDelPartido(LadoDelEquipo.Local, "Jugador Uno", "JUG-009", 2),
                ],
            },
            _ => throw new InvalidOperationException(caso),
        };

        Assert.Equal(Huella(@base), Huella(mismo));
        Assert.Equal(EntradaCanonica.Construir(@base).Bytes(), EntradaCanonica.Construir(mismo).Bytes());
    }
}
