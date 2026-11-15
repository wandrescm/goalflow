using GoalFlow.Application.Torneos.Contracts;

namespace GoalFlow.Application.Estadisticas.Calculo;

/// <summary>Lo que un partido aporta a la tabla de un equipo (Spec 009 R-13, R-14). <c>Deducciones</c> va aparte de los puntos (R-16).</summary>
internal readonly record struct Aporte(int Pj, int Pg, int Pe, int Pp, int Gf, int Gc, int PuntosPorResultado, int Deducciones);

/// <summary>
/// Reglas de qué aporta cada partido, comunes a las tres calculadoras. Puro: sin E/S, sin reloj, sin cultura. Un dato de
/// configuración ausente nunca se reemplaza por un valor por defecto: se lanza <see cref="InvalidOperationException"/>, porque
/// el caso de uso ya respondió <c>409 configuracion_pendiente</c> antes de calcular (R-25).
/// </summary>
internal static class AporteDePartido
{
    public static AdjudicacionParaEstadisticas Adjudicacion(PartidoParaEstadisticas partido, ReglamentoParaEstadisticas reglamento)
    {
        var motivo = partido.Planilla?.Motivo;
        if (motivo is not null && reglamento.Adjudicacion is { } porMotivo && porMotivo.TryGetValue(motivo, out var valores)) return valores;
        throw new InvalidOperationException("Falta la configuración de adjudicación que el cálculo necesita.");
    }

    /// <summary>La tabla: lo que el partido aporta a cada lado, o <c>null</c> si no aporta nada (R-12).</summary>
    public static (Aporte Local, Aporte Visitante)? Tabla(PartidoParaEstadisticas partido, ReglamentoParaEstadisticas reglamento)
    {
        var puntos = reglamento.Puntos ?? throw new InvalidOperationException("Faltan los puntos que el cálculo necesita.");
        var planilla = partido.Planilla;
        if (planilla is null) return null;

        switch (partido.Estado)
        {
            case EstadoDePartidoParaEstadisticas.Jugado when planilla.MarcadorLocal is { } ml && planilla.MarcadorVisitante is { } mv:
            {
                var (pl, pv) = PuntosDe(ml, mv, puntos.Victoria, puntos.Empate, puntos.Derrota);
                return (Lado(ml, mv, pl, 0), Lado(mv, ml, pv, 0));
            }
            case EstadoDePartidoParaEstadisticas.Adjudicado when planilla.OficialLocal is { } ol && planilla.OficialVisitante is { } ov:
            {
                var a = Adjudicacion(partido, reglamento);
                var (pl, pv) = PuntosDe(ol, ov, a.PuntosGanador, a.PuntosEmpate, a.PuntosPerdedor);
                var (dl, dv) = ol == ov ? (a.DeduccionEmpate, a.DeduccionEmpate) : ol > ov ? (0, a.DeduccionPerdedor) : (a.DeduccionPerdedor, 0);

                // (c) partido jugado, ganado, empatado o perdido solo si cuentaComoJugado; (d) el marcador oficial en GF y GC solo si golesOficialesEnTabla.
                var local = Lado(a.GolesOficialesEnTabla ? ol : 0, a.GolesOficialesEnTabla ? ov : 0, pl, dl, a.CuentaComoJugado ? Resultado(ol, ov) : 0, a.CuentaComoJugado);
                var visitante = Lado(a.GolesOficialesEnTabla ? ov : 0, a.GolesOficialesEnTabla ? ol : 0, pv, dv, a.CuentaComoJugado ? Resultado(ov, ol) : 0, a.CuentaComoJugado);
                return (local, visitante);
            }
            default:
                return null;
        }
    }

    /// <summary>1 si <paramref name="propios"/> gana, 0 si empata, −1 si pierde.</summary>
    private static int Resultado(int propios, int rivales) => propios > rivales ? 1 : propios == rivales ? 0 : -1;

    /// <summary>Los puntos por resultado de cada lado según su marcador (R-14 a).</summary>
    private static (int Local, int Visitante) PuntosDe(int local, int visitante, int ganador, int empate, int perdedor) =>
        local > visitante ? (ganador, perdedor) : local == visitante ? (empate, empate) : (perdedor, ganador);

    private static Aporte Lado(int propios, int rivales, int puntos, int deducciones) =>
        Lado(propios, rivales, puntos, deducciones, Resultado(propios, rivales), true);

    private static Aporte Lado(int propios, int rivales, int puntos, int deducciones, int resultado, bool cuentaComoJugado) =>
        cuentaComoJugado
            ? new Aporte(1, resultado > 0 ? 1 : 0, resultado == 0 ? 1 : 0, resultado < 0 ? 1 : 0, propios, rivales, puntos, deducciones)
            : new Aporte(0, 0, 0, 0, propios, rivales, puntos, deducciones);

    /// <summary>Si las tarjetas del partido cuentan para el fair play, y si fueron informadas (R-13, R-14 g).</summary>
    public static (bool Cuenta, bool Informadas) Tarjetas(PartidoParaEstadisticas partido, ReglamentoParaEstadisticas reglamento)
    {
        var planilla = partido.Planilla;
        if (planilla is null) return (false, false);
        switch (partido.Estado)
        {
            case EstadoDePartidoParaEstadisticas.Jugado:
                return (true, planilla.TarjetasInformadas == true);
            case EstadoDePartidoParaEstadisticas.Adjudicado when planilla.MarcadorLocal is not null:
                return Adjudicacion(partido, reglamento).TarjetasEnFairPlay ? (true, planilla.TarjetasInformadas == true) : (false, false);
            default:
                return (false, false);
        }
    }

    /// <summary>Si las filas de goles del partido cuentan para los goleadores (R-13, R-14 f).</summary>
    public static bool CuentaParaGoleadores(PartidoParaEstadisticas partido, ReglamentoParaEstadisticas reglamento) =>
        partido.Planilla is { } planilla && partido.Estado switch
        {
            EstadoDePartidoParaEstadisticas.Jugado => planilla.MarcadorLocal is not null,
            EstadoDePartidoParaEstadisticas.Adjudicado =>
                planilla.MarcadorLocal is not null && Adjudicacion(partido, reglamento).GolesDeportivosEnGoleadores,
            _ => false,
        };

    /// <summary>Los goles en contra de cada lado para la valla, o <c>null</c> si el partido no cuenta (R-13, R-14 e).</summary>
    public static (int Local, int Visitante)? Valla(PartidoParaEstadisticas partido, ReglamentoParaEstadisticas reglamento)
    {
        var planilla = partido.Planilla;
        if (planilla is null) return null;
        switch (partido.Estado)
        {
            case EstadoDePartidoParaEstadisticas.Jugado when planilla.MarcadorLocal is { } ml && planilla.MarcadorVisitante is { } mv:
                return (mv, ml);
            case EstadoDePartidoParaEstadisticas.Adjudicado when planilla.OficialLocal is { } ol && planilla.OficialVisitante is { } ov:
                return Adjudicacion(partido, reglamento).GolesOficialesEnValla ? (ov, ol) : null;
            default:
                return null;
        }
    }
}
