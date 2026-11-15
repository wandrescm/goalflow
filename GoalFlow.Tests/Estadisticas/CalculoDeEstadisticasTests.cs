using System.Text.Json;
using GoalFlow.Application.Estadisticas.Calculo;
using GoalFlow.Application.Torneos.Contracts;
using static GoalFlow.Tests.Estadisticas.FixturesDelApendice;

namespace GoalFlow.Tests.Estadisticas;

/// <summary>
/// Spec 009 T-01 a T-08 y T-19 (unitaria): el cálculo puro sobre los fixtures del Apéndice A. Cada valor esperado está escrito
/// aquí desde el Apéndice, nunca copiado de la salida del código. Si un resultado difiere del Apéndice, se detiene la fase.
/// </summary>
public class CalculoDeEstadisticasTests
{
    /// <summary>Fila esperada en el orden del Apéndice: pos, equipo, PJ, G, E, P, GF, GC, DG, PPR, Ded, Pts, TC, TS.</summary>
    private sealed record Esperada(int Pos, string Equipo, int Pj, int G, int E, int P, int Gf, int Gc, int Dg, int Ppr, int Ded, int Pts, int Tc, int Ts);

    private static Esperada Fila(FilaDePosicion f) => new(
        f.Posicion, f.Equipo.Codigo, f.Pj, f.Pg, f.Pe, f.Pp, f.Gf, f.Gc, f.Dg, f.PuntosPorResultado, f.Deducciones, f.Puntos,
        f.PartidosConTarjetasComputables, f.PartidosConTarjetasSinInformar);

    private static void Filas(IReadOnlyList<Esperada> esperadas, ResultadoDePosiciones real) =>
        Assert.Equal(esperadas, real.Filas.Select(Fila).ToList());

    private static ResultadoDePosiciones Posiciones(DatosDeTorneoParaEstadisticas datos) => CalculadoraDePosiciones.Calcular(datos, 1);

    // ---- T-01: F1, recursivo y no recursivo ----

    [Fact]
    public void T01_F1_recursivo_da_A_B_C_D_con_las_filas_del_Apendice()
    {
        var r = Posiciones(F1(recursivo: true));

        Filas(
        [
            new(1, "A", 3, 2, 0, 1, 7, 2, 5, 6, 0, 6, 3, 0),
            new(2, "B", 3, 2, 0, 1, 6, 4, 2, 6, 0, 6, 3, 0),
            new(3, "C", 3, 2, 0, 1, 8, 4, 4, 6, 0, 6, 3, 0),
            new(4, "D", 3, 0, 0, 3, 0, 11, -11, 0, 0, 0, 3, 0),
        ], r);
        Assert.Empty(r.Empates);
    }

    [Fact]
    public void T01_F1_no_recursivo_da_A_C_B_D_con_las_mismas_filas()
    {
        var r = Posiciones(F1(recursivo: false));

        Filas(
        [
            new(1, "A", 3, 2, 0, 1, 7, 2, 5, 6, 0, 6, 3, 0),
            new(2, "C", 3, 2, 0, 1, 8, 4, 4, 6, 0, 6, 3, 0),
            new(3, "B", 3, 2, 0, 1, 6, 4, 2, 6, 0, 6, 3, 0),
            new(4, "D", 3, 0, 0, 3, 0, 11, -11, 0, 0, 0, 3, 0),
        ], r);
        Assert.Empty(r.Empates);
    }

    // ---- T-02: F2 termina ----

    [Fact]
    public void T02_F2_termina_con_A_B_y_C_en_la_posicion_1_y_un_empate_sin_omisiones()
    {
        var r = Posiciones(F2());

        Filas(
        [
            new(1, "A", 2, 1, 0, 1, 1, 1, 0, 3, 0, 3, 2, 0),
            new(1, "B", 2, 1, 0, 1, 1, 1, 0, 3, 0, 3, 2, 0),
            new(1, "C", 2, 1, 0, 1, 1, 1, 0, 3, 0, 3, 2, 0),
        ], r);
        var empate = Assert.Single(r.Empates);
        Assert.Equal(1, empate.Posicion);
        Assert.Equal(["A", "B", "C"], empate.Equipos);
        Assert.Equal("criterios_agotados", empate.Motivo);
        Assert.Empty(empate.CriteriosOmitidos);
    }

    // ---- T-03: F3a y F3b ----

    [Fact]
    public void T03_F3a_da_Y_1_B_2_C_2_X_4_con_FairPlay_omitido_para_B_y_cobertura_6_de_2()
    {
        var r = Posiciones(F3(todoInformado: false));

        Filas(
        [
            new(1, "Y", 3, 2, 1, 0, 2, 0, 2, 7, 0, 7, 3, 1),
            new(2, "B", 3, 1, 1, 1, 2, 2, 0, 4, 0, 4, 3, 2),
            new(2, "C", 3, 1, 1, 1, 2, 2, 0, 4, 0, 4, 3, 0),
            new(4, "X", 3, 0, 1, 2, 0, 2, -2, 1, 0, 1, 3, 1),
        ], r);
        var empate = Assert.Single(r.Empates);
        Assert.Equal(2, empate.Posicion);
        Assert.Equal(["B", "C"], empate.Equipos);
        Assert.Equal("criterios_agotados", empate.Motivo);
        var omitido = Assert.Single(empate.CriteriosOmitidos);
        Assert.Equal("FairPlay", omitido.Criterio);
        Assert.Equal("tarjetas_no_informadas", omitido.Motivo);
        var equipo = Assert.Single(omitido.Equipos);
        Assert.Equal(("B", 2), (equipo.Codigo, equipo.PartidosSinInformar));
        Assert.Equal(new CoberturaDeTarjetas(6, 2), r.Cobertura);
    }

    [Fact]
    public void T03_F3b_con_todo_informado_da_Y_1_B_2_C_3_X_4_sin_empates()
    {
        var r = Posiciones(F3(todoInformado: true));

        Assert.Equal([(1, "Y"), (2, "B"), (3, "C"), (4, "X")], r.Filas.Select(f => (f.Posicion, f.Equipo.Codigo)).ToList());
        Assert.All(r.Filas, f => Assert.Equal(0, f.PartidosConTarjetasSinInformar));
        Assert.Empty(r.Empates);
        Assert.Equal(new CoberturaDeTarjetas(6, 0), r.Cobertura);
    }

    // ---- T-04: F4a, F4b y F4c ----

    [Fact]
    public void T04_F4a_walkover_3_0_con_WO_A()
    {
        var datos = F4(WoA, 3, 0);

        Filas(
        [
            new(1, "A", 1, 1, 0, 0, 3, 0, 3, 3, 0, 3, 0, 0),
            new(2, "B", 1, 0, 0, 1, 0, 3, -3, 0, 0, 0, 0, 0),
        ], Posiciones(datos));
        Assert.Equal([(1, "A", 1, 0), (2, "B", 1, 3)],
            CalculadoraDeValla.Calcular(datos).Filas.Select(f => (f.Posicion, f.Equipo.Codigo, f.PartidosComputados, f.GolesEnContra)).ToList());
    }

    [Fact]
    public void T04_F4b_sin_efectos_en_la_tabla_deduce_un_punto_a_B_y_la_valla_queda_vacia()
    {
        var datos = F4(WoA with { DeduccionPerdedor = 1, CuentaComoJugado = false, GolesOficialesEnTabla = false, GolesOficialesEnValla = false }, 3, 0);

        Filas(
        [
            new(1, "A", 0, 0, 0, 0, 0, 0, 0, 3, 0, 3, 0, 0),
            new(2, "B", 0, 0, 0, 0, 0, 0, 0, 0, 1, -1, 0, 0),
        ], Posiciones(datos));
        Assert.Empty(CalculadoraDeValla.Calcular(datos).Filas);
    }

    [Fact]
    public void T04_F4c_oficial_0_0_con_puntosEmpate_0_y_deduccionEmpate_1_deja_a_los_dos_en_menos_1_empatados()
    {
        var datos = F4(WoA with { PuntosEmpate = 0, DeduccionEmpate = 1 }, 0, 0);

        var r = Posiciones(datos);

        Filas(
        [
            new(1, "A", 1, 0, 1, 0, 0, 0, 0, 0, 1, -1, 0, 0),
            new(1, "B", 1, 0, 1, 0, 0, 0, 0, 0, 1, -1, 0, 0),
        ], r);
        var empate = Assert.Single(r.Empates);
        Assert.Equal((1, "criterios_agotados"), (empate.Posicion, empate.Motivo));
        Assert.Equal(["A", "B"], empate.Equipos);
        Assert.Empty(empate.CriteriosOmitidos);
        Assert.Equal([(1, "A", 1, 0), (1, "B", 1, 0)],
            CalculadoraDeValla.Calcular(datos).Filas.Select(f => (f.Posicion, f.Equipo.Codigo, f.PartidosComputados, f.GolesEnContra)).ToList());
    }

    // ---- T-05: F5a y F5b ----

    [Fact]
    public void T05_F5a_la_tabla_sigue_el_oficial_0_3_y_los_goleadores_el_marcador_deportivo()
    {
        var datos = F5(WoA);

        Filas(
        [
            new(1, "B", 1, 1, 0, 0, 3, 0, 3, 3, 0, 3, 1, 0),
            new(2, "A", 1, 0, 0, 1, 0, 3, -3, 0, 0, 0, 1, 0),
        ], Posiciones(datos));

        var g = CalculadoraDeGoleadores.Calcular(datos);
        Assert.Equal([(1, "PA1", "A", 2), (2, "PB1", "B", 1), (2, "PB2", "B", 1)],
            g.Filas.Select(f => (f.Posicion, f.Jugador.Codigo, f.Equipo.Codigo, f.Goles)).ToList());
        var c = g.Cobertura;
        Assert.Equal((1, 0, 4, 4, 0, 0, 4), (c.PartidosComputables, c.PartidosConGolesSinAutor, c.GolesTotales, c.GolesDeJugador, c.Autogoles, c.GolesSinAutor, c.GolesConAutor));
        Assert.Equal([("A", 1, 0, 0), ("B", 1, 0, 0)],
            c.PorEquipo.Select(e => (e.Equipo.Codigo, e.PartidosComputables, e.PartidosConGolesSinAutor, e.GolesSinAutor)).ToList());
    }

    [Fact]
    public void T05_F5b_sin_golesDeportivosEnGoleadores_no_hay_goleadores_y_la_cobertura_es_cero()
    {
        var g = CalculadoraDeGoleadores.Calcular(F5(WoA with { GolesDeportivosEnGoleadores = false }));

        Assert.Empty(g.Filas);
        var c = g.Cobertura;
        Assert.Equal((0, 0, 0, 0, 0, 0, 0), (c.PartidosComputables, c.PartidosConGolesSinAutor, c.GolesTotales, c.GolesDeJugador, c.Autogoles, c.GolesSinAutor, c.GolesConAutor));
        Assert.Empty(c.PorEquipo);
    }

    // ---- T-06: F6 ----

    [Fact]
    public void T06_F6_las_posiciones_sin_empates_y_la_cobertura_de_tarjetas_9_de_2()
    {
        var r = Posiciones(F6());

        Filas(
        [
            new(1, "E1", 5, 3, 2, 0, 7, 3, 4, 11, 0, 11, 5, 2),
            new(2, "E2", 5, 1, 2, 2, 5, 7, -2, 5, 0, 5, 5, 0),
            new(3, "E3", 3, 1, 1, 1, 5, 3, 2, 4, 0, 4, 2, 0),
            new(4, "E5", 2, 1, 0, 1, 3, 3, 0, 3, 0, 3, 2, 1),
            new(5, "E6", 2, 0, 2, 0, 2, 2, 0, 2, 0, 2, 2, 0),
            new(6, "E4", 3, 0, 1, 2, 0, 4, -4, 1, 0, 1, 2, 1),
        ], r);
        Assert.Empty(r.Empates);
        Assert.Equal(new CoberturaDeTarjetas(9, 2), r.Cobertura);
    }

    [Fact]
    public void T06_F6_los_goleadores_con_su_cobertura_y_el_invariante_de_R_23()
    {
        var g = CalculadoraDeGoleadores.Calcular(F6());

        Assert.Equal(
            [(1, "P21", "E2", 4), (2, "P51", "E5", 3), (3, "P11", "E1", 2), (3, "P12", "E1", 2), (5, "P22", "E2", 1), (5, "P61", "E6", 1), (5, "P62", "E6", 1)],
            g.Filas.Select(f => (f.Posicion, f.Jugador.Codigo, f.Equipo.Codigo, f.Goles)).ToList());
        var c = g.Cobertura;
        Assert.Equal((9, 3, 19, 14, 1, 4, 15), (c.PartidosComputables, c.PartidosConGolesSinAutor, c.GolesTotales, c.GolesDeJugador, c.Autogoles, c.GolesSinAutor, c.GolesConAutor));
        Assert.Equal(
            [("E1", 5, 2, 2), ("E2", 5, 0, 0), ("E3", 2, 1, 2), ("E4", 2, 0, 0), ("E5", 2, 0, 0), ("E6", 2, 0, 0)],
            c.PorEquipo.Select(e => (e.Equipo.Codigo, e.PartidosComputables, e.PartidosConGolesSinAutor, e.GolesSinAutor)).ToList());
        // golesDeJugador es exactamente la suma de las filas, y se cumple el invariante.
        Assert.Equal(c.GolesDeJugador, g.Filas.Sum(f => f.Goles));
        Assert.Equal(c.GolesTotales, c.GolesDeJugador + c.Autogoles + c.GolesSinAutor);
    }

    [Fact]
    public void T06_F6_la_valla()
    {
        var v = CalculadoraDeValla.Calcular(F6());

        Assert.Equal(
            [(1, "E6", 2, 2), (2, "E1", 5, 3), (2, "E3", 3, 3), (2, "E5", 2, 3), (5, "E4", 3, 4), (6, "E2", 5, 7)],
            v.Filas.Select(f => (f.Posicion, f.Equipo.Codigo, f.PartidosComputados, f.GolesEnContra)).ToList());
    }

    // ---- T-07: F7, dos vueltas ----

    [Fact]
    public void T07_F7_dos_vueltas_da_C_1_y_B_2()
    {
        var r = Posiciones(F7());

        Filas(
        [
            new(1, "C", 2, 1, 0, 1, 2, 1, 1, 3, 0, 3, 2, 0),
            new(2, "B", 2, 1, 0, 1, 1, 2, -1, 3, 0, 3, 2, 0),
        ], r);
        Assert.Empty(r.Empates);
    }

    // ---- T-08: orden ordinal ----

    /// <summary>a1 1–1 B1, con un gol de j1 (a1) y uno de K1 (B1): empatados en tabla, valla y goleadores.</summary>
    private static DatosDeTorneoParaEstadisticas Ordinal() => Torneo(
        ["a1", "B1"],
        [Jugado("a1", "B1", 1, 1, filas: [DeJugador("a1", "j1", 1), DeJugador("B1", "K1", 1)])],
        Reglas([], false),
        jugadores: [Jug("j1", "a1"), Jug("K1", "B1")]);

    [Fact]
    public void T08_los_empates_se_ordenan_con_comparacion_ordinal_B1_antes_que_a1_y_K1_antes_que_j1()
    {
        var datos = Ordinal();

        Assert.Equal([(1, "B1"), (1, "a1")], Posiciones(datos).Filas.Select(f => (f.Posicion, f.Equipo.Codigo)).ToList());
        Assert.Equal([(1, "B1"), (1, "a1")], CalculadoraDeValla.Calcular(datos).Filas.Select(f => (f.Posicion, f.Equipo.Codigo)).ToList());
        Assert.Equal([(1, "K1"), (1, "j1")], CalculadoraDeGoleadores.Calcular(datos).Filas.Select(f => (f.Posicion, f.Jugador.Codigo)).ToList());
        Assert.Equal(["B1", "a1"], Assert.Single(Posiciones(datos).Empates).Equipos);
    }

    // ---- T-19 (unitaria): el orden de inserción no cambia nada ----

    private static string Serializar(DatosDeTorneoParaEstadisticas datos) => JsonSerializer.Serialize(new
    {
        posiciones = Posiciones(datos),
        goleadores = CalculadoraDeGoleadores.Calcular(datos),
        valla = CalculadoraDeValla.Calcular(datos),
    });

    private static DatosDeTorneoParaEstadisticas EnOrdenInverso(DatosDeTorneoParaEstadisticas datos) => datos with
    {
        Fases = [.. datos.Fases.Reverse()],
        Equipos = [.. datos.Equipos.Reverse()],
        Jugadores = [.. datos.Jugadores.Reverse()],
        Partidos = [.. datos.Partidos.Reverse().Select(p => p with
        {
            Planilla = p.Planilla is null ? null : p.Planilla with
            {
                Goles = [.. p.Planilla.Goles.Reverse()],
                Tarjetas = [.. p.Planilla.Tarjetas.Reverse()],
            },
        })],
    };

    [Fact]
    public void T19_F6_con_partidos_jugadores_y_filas_en_orden_inverso_da_resultados_identicos_byte_a_byte()
    {
        var datos = F6();

        Assert.Equal(Serializar(datos), Serializar(EnOrdenInverso(datos)));
    }

    [Fact]
    public void T19_los_empates_de_F2_y_de_los_codigos_ordinales_no_dependen_del_orden_de_insercion()
    {
        foreach (var datos in new[] { F2(), Ordinal(), F3(todoInformado: false) })
            Assert.Equal(Serializar(datos), Serializar(EnOrdenInverso(datos)));
    }
}
