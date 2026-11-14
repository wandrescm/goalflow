using GoalFlow.Application.IA;
using GoalFlow.Application.IA.Verificacion;
using GoalFlow.Application.Torneos.Contracts;

namespace GoalFlow.Tests.IA.Verificacion;

/// <summary>
/// El generador de planillas de T-04 (Spec 008 C-12): semilla fija, y solo planillas que Torneos aceptaría. Verifica sus invariantes
/// (Spec 006 R-11 a, c, e, j y R-12 g a i) sobre cada planilla que emite; si una se rompe, falla con un mensaje propio del generador.
/// </summary>
internal sealed class GeneradorDePlanillas(int semilla)
{
    private readonly Random _azar = new(semilla);

    // Nombres de la Spec 008 T-04; los equipos y los jugadores salen de la misma lista, sin repetirse dentro de una planilla.
    private static readonly string[] Nombres =
    [
        "Jugador Uno", "Jugador Dos", "Equipo Diez", "Los Tres Reyes", "Club Minuto", "Penal FC", "Lluvia", "Ñandú",
        "de la Cruz", "Atl. Uno", "Equipo Uno", "Equipo Dos", "Jugador Tres", "Jugador Cuatro", "Cancha Norte", "Jugador Cinco",
    ];

    private static readonly string[] Torneos = ["Torneo Uno", "Copa 2026", "Liga Minuto", "Torneo de Lluvia"];
    private static readonly string[] Colores = [CodigosDeTorneos.Amarilla, CodigosDeTorneos.Roja, CodigosDeTorneos.Azul];

    public DatosDePartidoParaResumen Siguiente()
    {
        var nombres = Nombres.OrderBy(_ => _azar.Next()).ToList();
        var local = nombres[0];
        var visitante = nombres[1];
        var jugadores = new[] { nombres.Skip(2).Take(4).ToList(), nombres.Skip(6).Take(4).ToList() }; // [lado][i]

        var adjudicado = _azar.Next(4) == 0;
        var conMarcador = !adjudicado || _azar.Next(2) == 0;

        MarcadorDelPartido? marcador = null;
        var goles = new List<GolDelPartido>();
        var autogoles = new List<AutogolDelPartido>();
        var sinAutor = new MarcadorDelPartido(0, 0);
        bool? tarjetasInformadas = null;
        var tarjetas = new List<TarjetaDelPartido>();

        if (conMarcador)
        {
            while (true)
            {
                goles.Clear();
                autogoles.Clear();
                var puntos = new int[2];
                var sin = new int[2];
                for (var lado = 0; lado < 2; lado++)
                {
                    var l = lado == 0 ? LadoDelEquipo.Local : LadoDelEquipo.Visitante;
                    foreach (var j in jugadores[lado].OrderBy(_ => _azar.Next()).Take(_azar.Next(4)))
                    {
                        var cantidad = 1 + _azar.Next(2);
                        goles.Add(new GolDelPartido(l, j, "INS-" + j, cantidad));
                        puntos[lado] += cantidad;
                    }

                    sin[lado] = _azar.Next(3) == 0 ? 1 + _azar.Next(2) : 0;
                    puntos[lado] += sin[lado];
                }

                // Los autogoles de un jugador de un lado se acreditan al otro.
                for (var lado = 0; lado < 2; lado++)
                {
                    var autor = lado == 0 ? LadoDelEquipo.Local : LadoDelEquipo.Visitante;
                    foreach (var j in jugadores[lado].OrderBy(_ => _azar.Next()).Take(_azar.Next(4)))
                    {
                        var cantidad = 1 + _azar.Next(2);
                        autogoles.Add(new AutogolDelPartido(j, "INS-" + j, autor, cantidad));
                        puntos[1 - lado] += cantidad;
                    }
                }

                if (puntos[0] <= 6 && puntos[1] <= 6)
                {
                    marcador = new MarcadorDelPartido(puntos[0], puntos[1]);
                    sinAutor = new MarcadorDelPartido(sin[0], sin[1]);
                    break;
                }
            }

            tarjetasInformadas = _azar.Next(3) != 0 ? true : false;
            if (tarjetasInformadas == true)
            {
                foreach (var color in Colores)
                {
                    var todos = jugadores.SelectMany((js, lado) => js.Select(j => (j, lado))).OrderBy(_ => _azar.Next()).Take(_azar.Next(5));
                    foreach (var (j, lado) in todos)
                        tarjetas.Add(new TarjetaDelPartido(lado == 0 ? LadoDelEquipo.Local : LadoDelEquipo.Visitante, j, "INS-" + j, color, 1 + _azar.Next(3)));
                }
            }
        }

        var datos = new DatosDePartidoParaResumen(
            TorneoId: Guid.Parse("00000000-0000-0000-0000-0000000000a1"),
            Version: 1,
            Estado: adjudicado ? CodigosDeTorneos.Adjudicado : CodigosDeTorneos.Jugado,
            Torneo: Torneos[_azar.Next(Torneos.Length)],
            Fase: 1 + _azar.Next(3),
            Jornada: 1 + _azar.Next(12),
            Local: local,
            Visitante: visitante,
            Marcador: marcador,
            Oficial: adjudicado ? new MarcadorDelPartido(_azar.Next(7), _azar.Next(7)) : null,
            Motivo: adjudicado ? new[] { CodigosDeTorneos.WalkOver, CodigosDeTorneos.Sancion, CodigosDeTorneos.Otro }[_azar.Next(3)] : null,
            Goles: goles,
            Autogoles: autogoles,
            GolesSinAutor: sinAutor,
            TarjetasInformadas: tarjetasInformadas,
            Tarjetas: tarjetas,
            MotivoDeRectificacion: null,
            ProgramadoPara: new DateTime(2026, 11, 14, 15, 0, 0, DateTimeKind.Utc));

        VerificarInvariantes(datos);
        return datos;
    }

    /// <summary>Las invariantes de la planilla (C-12). Lanza una excepción con un mensaje propio del generador si una se rompe.</summary>
    public static void VerificarInvariantes(DatosDePartidoParaResumen d)
    {
        void Exigir(bool condicion, string regla)
        {
            if (!condicion) throw new InvalidOperationException($"GENERADOR DE T-04: la planilla emitida rompe {regla}; una entrada imposible no puede ser lo que el canario comprueba.");
        }

        // Spec 006 R-11 (a) y (c)
        if (d.Estado == CodigosDeTorneos.Jugado)
        {
            Exigir(d.Marcador is not null, "R-11 (a): un Jugado exige marcador");
            Exigir(d.Oficial is null && d.Motivo is null, "R-11 (a): un Jugado no lleva oficial ni motivo");
        }
        else
        {
            Exigir(d.Estado == CodigosDeTorneos.Adjudicado, "R-11: solo Jugado y Adjudicado tienen plantilla");
            Exigir(d.Oficial is not null, "R-11 (c): un Adjudicado exige oficial");
            Exigir(d.Motivo is CodigosDeTorneos.WalkOver or CodigosDeTorneos.Sancion or CodigosDeTorneos.Otro, "R-11 (c): motivo de adjudicación");
        }

        // R-11 (e): goles y tarjetas solo con marcador.
        if (d.Marcador is null)
        {
            Exigir(d.Goles.Count == 0 && d.Autogoles.Count == 0 && d.Tarjetas.Count == 0, "R-11 (e): goles y tarjetas solo con marcador");
            Exigir(d.GolesSinAutor is { Local: 0, Visitante: 0 }, "R-11 (e): goles sin autor solo con marcador");
        }

        // R-11 (j): tarjetasInformadas es obligatoria con marcador y ausente sin él; las tarjetas solo si es true.
        Exigir((d.Marcador is null) == (d.TarjetasInformadas is null), "R-11 (j): tarjetasInformadas con marcador y solo con marcador");
        Exigir(d.Tarjetas.Count == 0 || d.TarjetasInformadas == true, "R-11 (j): tarjetas solo si tarjetasInformadas es true");

        // R-12 (g): por lado, goles de jugador + autogoles del otro lado + sin autor = marcador.
        if (d.Marcador is not null)
        {
            foreach (var lado in new[] { LadoDelEquipo.Local, LadoDelEquipo.Visitante })
            {
                var suma = d.Goles.Where(g => g.Equipo == lado).Sum(g => g.Cantidad)
                    + d.Autogoles.Where(a => EntradaCanonica.Contrario(a.EquipoDelAutor) == lado).Sum(a => a.Cantidad)
                    + (lado == LadoDelEquipo.Local ? d.GolesSinAutor.Local : d.GolesSinAutor.Visitante);
                Exigir(suma == (lado == LadoDelEquipo.Local ? d.Marcador.Local : d.Marcador.Visitante), $"R-12 (g): la suma de goles del lado {lado} no es su marcador");
            }
        }

        // R-12 (h): un jugador una vez en goles, una en autogoles y una por color en tarjetas.
        Exigir(d.Goles.Select(g => g.CodigoDeInscripcion).Distinct().Count() == d.Goles.Count, "R-12 (h): un jugador repetido en goles");
        Exigir(d.Autogoles.Select(a => a.CodigoDeInscripcion).Distinct().Count() == d.Autogoles.Count, "R-12 (h): un jugador repetido en autogoles");
        Exigir(d.Tarjetas.Select(t => (t.CodigoDeInscripcion, t.Color)).Distinct().Count() == d.Tarjetas.Count, "R-12 (h): un jugador repetido por color en tarjetas");

        // R-12 (i): marcadores 0 a 99, cantidades de gol >= 1, de tarjeta 1 a 9, sin autor >= 0.
        foreach (var m in new[] { d.Marcador, d.Oficial })
            Exigir(m is null || (m.Local is >= 0 and <= 99 && m.Visitante is >= 0 and <= 99), "R-12 (i): marcador fuera de 0 a 99");
        Exigir(d.Goles.All(g => g.Cantidad >= 1) && d.Autogoles.All(a => a.Cantidad >= 1), "R-12 (i): cantidad de gol menor que 1");
        Exigir(d.Tarjetas.All(t => t.Cantidad is >= 1 and <= 9), "R-12 (i): cantidad de tarjeta fuera de 1 a 9");
        Exigir(d.GolesSinAutor.Local >= 0 && d.GolesSinAutor.Visitante >= 0, "R-12 (i): goles sin autor negativos");

        // Un nombre no puede ser a la vez el de un equipo y el de un jugador, ni el de dos jugadores.
        var jugadores = d.Goles.Select(g => g.Jugador).Concat(d.Autogoles.Select(a => a.Jugador)).Concat(d.Tarjetas.Select(t => t.Jugador)).Distinct().ToList();
        Exigir(!jugadores.Contains(d.Local) && !jugadores.Contains(d.Visitante) && d.Local != d.Visitante, "los nombres de equipos y jugadores son distintos");
    }
}

/// <summary>Spec 008 T-04 (R-12): el verificador aprueba el 100 % de las plantillas y de los borradores extendidos que A-01 puede producir.</summary>
public class CanarioDelVerificadorTests
{
    private const int LongitudMaximaDelBorrador = 4000;

    // Los textos que la Spec 007 R-11 produce para una planilla: la plantilla de su estado y, si es Jugado, el borrador extendido.
    private static IEnumerable<(string Nombre, string Texto)> Textos(DatosDePartidoParaResumen datos, EntradaCanonica entrada)
    {
        yield return ("plantilla", PlantillasDeResumen.Plantilla(entrada));
        if (datos.Estado == CodigosDeTorneos.Jugado)
        {
            var borrador = PlantillasDeResumen.BorradorExtendido(entrada, LongitudMaximaDelBorrador);
            // El borrador compacto queda fuera de R-12 (C-08): este canario no debe llegar a él.
            Assert.DoesNotContain(PlantillasDeResumen.TextoDelBorradorCompacto, borrador, StringComparison.Ordinal);
            yield return ("borrador extendido", borrador);
        }
    }

    private static void AprobarTodos(string origen, DatosDePartidoParaResumen datos)
    {
        var entrada = EntradaCanonica.Construir(datos);
        foreach (var (nombre, texto) in Textos(datos, entrada))
        {
            var resultado = VerificadorDeResumen.Verificar(texto, entrada);
            Assert.True(resultado.Aprobado, $"{origen}: el {nombre} fue rechazado ({string.Join(",", resultado.Reglas)}): «{texto}» — el defecto está en el verificador.");
        }
    }

    // ---- (a) Las planillas de Spec 007 T-04, salvo el borrador compacto ----

    private static DatosDePartidoParaResumen Jugado(int local, int visitante) =>
        DatosDePrueba.Vacio() with { Marcador = new MarcadorDelPartido(local, visitante) };

    private static DatosDePartidoParaResumen Adjudicado(string motivo, bool conDeportivo) => DatosDePrueba.Vacio(CodigosDeTorneos.Adjudicado) with
    {
        Marcador = conDeportivo ? new MarcadorDelPartido(1, 4) : null,
        Oficial = new MarcadorDelPartido(3, 0),
        Motivo = motivo,
    };

    public static IEnumerable<object[]> PlanillasDeSpec007T04()
    {
        yield return ["Jugado 0-0", Jugado(0, 0)];
        yield return ["Jugado 2-1 con goles sin autor", Jugado(2, 1) with { GolesSinAutor = new MarcadorDelPartido(2, 1) }];
        yield return ["Jugado 2-1 con tarjetas no informadas", Jugado(2, 1) with { GolesSinAutor = new MarcadorDelPartido(2, 1), TarjetasInformadas = false }];
        yield return ["Jugado 0-0 con tarjetas no informadas", Jugado(0, 0) with { TarjetasInformadas = false }];
        foreach (var motivo in new[] { CodigosDeTorneos.WalkOver, CodigosDeTorneos.Sancion, CodigosDeTorneos.Otro })
        {
            yield return [$"Adjudicado {motivo} sin deportivo", Adjudicado(motivo, false)];
            yield return [$"Adjudicado {motivo} con deportivo", Adjudicado(motivo, true)];
        }

        yield return ["Jugado con autogol y amarilla", DatosDePrueba.ConAutogol()];
        yield return ["Jugado 4-3 con cantidades, sin autor y tarjetas de cada color", Jugado(4, 3) with
        {
            Goles =
            [
                new GolDelPartido(LadoDelEquipo.Local, "Jugador Dos", "JUG-002", 2),
                new GolDelPartido(LadoDelEquipo.Local, "Jugador Tres", "JUG-003", 1),
                new GolDelPartido(LadoDelEquipo.Visitante, "Jugador Cuatro", "JUG-004", 1),
            ],
            Autogoles = [new AutogolDelPartido("Jugador Uno", "JUG-001", LadoDelEquipo.Local, 2)],
            GolesSinAutor = new MarcadorDelPartido(1, 0),
            Tarjetas =
            [
                new TarjetaDelPartido(LadoDelEquipo.Local, "Jugador Dos", "JUG-002", CodigosDeTorneos.Roja, 2),
                new TarjetaDelPartido(LadoDelEquipo.Visitante, "Jugador Cuatro", "JUG-004", CodigosDeTorneos.Azul, 1),
                new TarjetaDelPartido(LadoDelEquipo.Visitante, "Jugador Cuatro", "JUG-004", CodigosDeTorneos.Amarilla, 1),
            ],
        }];
        yield return ["Jugado 0-2 con dos autogoles del mismo jugador", Jugado(0, 2) with
        {
            Autogoles = [new AutogolDelPartido("Jugador Uno", "JUG-001", LadoDelEquipo.Local, 2)],
            TarjetasInformadas = true,
        }];
        yield return ["Jugado 0-4 con goleador, dos autogoles y sin autor", Jugado(0, 4) with
        {
            Goles = [new GolDelPartido(LadoDelEquipo.Visitante, "Jugador Cuatro", "JUG-004", 1)],
            Autogoles =
            [
                new AutogolDelPartido("Jugador Uno", "JUG-001", LadoDelEquipo.Local, 1),
                new AutogolDelPartido("Jugador Dos", "JUG-002", LadoDelEquipo.Local, 1),
            ],
            GolesSinAutor = new MarcadorDelPartido(0, 1),
            TarjetasInformadas = true,
        }];
        yield return ["Jugado 1-0 con goles de un solo lado", Jugado(1, 0) with
        {
            Goles = [new GolDelPartido(LadoDelEquipo.Local, "Jugador Uno", "JUG-001", 1)],
            TarjetasInformadas = true,
        }];
        yield return ["Jugado 0-1 con autogol y tarjetas no informadas", Jugado(0, 1) with
        {
            Autogoles = [new AutogolDelPartido("Jugador Uno", "JUG-001", LadoDelEquipo.Local, 1)],
            TarjetasInformadas = false,
        }];
        yield return ["Plantillas más largas con nombres de 80 caracteres", DatosDePrueba.Vacio() with
        {
            Local = new string('A', 80),
            Visitante = new string('B', 80),
            Marcador = new MarcadorDelPartido(99, 99),
            GolesSinAutor = new MarcadorDelPartido(99, 99),
            TarjetasInformadas = false,
        }];
        yield return ["Adjudicado más largo con nombres de 80 caracteres", DatosDePrueba.Vacio(CodigosDeTorneos.Adjudicado) with
        {
            Local = new string('A', 80),
            Visitante = new string('B', 80),
            Marcador = null,
            Oficial = new MarcadorDelPartido(99, 99),
            Motivo = CodigosDeTorneos.Otro,
        }];
    }

    [Theory]
    [MemberData(nameof(PlanillasDeSpec007T04))]
    public void T04_el_verificador_aprueba_la_plantilla_y_el_borrador_extendido_de_cada_planilla_de_la_Spec_007(string nombre, DatosDePartidoParaResumen datos)
    {
        AprobarTodos(nombre, datos);
    }

    // ---- (b) 200 planillas generadas con semilla fija ----

    [Fact]
    public void T04_el_verificador_aprueba_las_plantillas_y_los_borradores_extendidos_de_200_planillas_generadas()
    {
        var generador = new GeneradorDePlanillas(semilla: 20261113);
        var estados = new HashSet<string>();
        var informadas = new HashSet<bool?>();
        var conTarjetas = 0;

        for (var i = 0; i < 200; i++)
        {
            var datos = generador.Siguiente();
            estados.Add(datos.Estado);
            informadas.Add(datos.TarjetasInformadas);
            if (datos.Tarjetas.Count > 0) conTarjetas++;
            AprobarTodos($"planilla generada {i}", datos);
        }

        // El generador cubre lo que la Spec pide: ambos estados, true, false y null, y tarjetas.
        Assert.Equal(2, estados.Count);
        Assert.Equal(3, informadas.Count);
        Assert.True(conTarjetas > 20, $"Solo {conTarjetas} planillas con tarjetas");
    }

    [Fact]
    public void T04_el_generador_es_determinista_con_la_misma_semilla()
    {
        var a = new GeneradorDePlanillas(7);
        var b = new GeneradorDePlanillas(7);

        for (var i = 0; i < 20; i++)
            Assert.Equal(EntradaCanonica.Construir(a.Siguiente()).Huella(), EntradaCanonica.Construir(b.Siguiente()).Huella());
    }

    [Fact]
    public void T04_el_generador_rechaza_con_su_propio_mensaje_una_planilla_imposible()
    {
        // Un 2-0 cuyos goles suman 1: rompe R-12 (g). El canario nunca debe comprobar una entrada así.
        var imposible = DatosDePrueba.Vacio() with
        {
            Marcador = new MarcadorDelPartido(2, 0),
            Goles = [new GolDelPartido(LadoDelEquipo.Local, "Jugador Uno", "INS-1", 1)],
        };

        var excepcion = Assert.Throws<InvalidOperationException>(() => GeneradorDePlanillas.VerificarInvariantes(imposible));

        Assert.StartsWith("GENERADOR DE T-04", excepcion.Message, StringComparison.Ordinal);
        Assert.Contains("R-12 (g)", excepcion.Message, StringComparison.Ordinal);
    }
}
