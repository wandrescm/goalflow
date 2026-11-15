using GoalFlow.Application.Torneos.Contracts;

namespace GoalFlow.Application.Estadisticas.Calculo;

/// <summary>
/// Posiciones de una fase (Spec 009 R-16 a R-21): puro, sin E/S, sin reloj y sin cultura. Todo orden de códigos es ordinal.
/// El orden se calcula por particiones (R-17): el primer criterio implícito es el puntaje, y cada criterio de la lista divide
/// un grupo todavía empatado.
/// </summary>
public static class CalculadoraDePosiciones
{
    public const string MotivoCriteriosAgotados = "criterios_agotados";
    public const string MotivoTarjetasNoInformadas = "tarjetas_no_informadas";

    public static ResultadoDePosiciones Calcular(DatosDeTorneoParaEstadisticas datos, int faseOrden)
    {
        var reglamento = datos.Reglamento ?? throw new InvalidOperationException("El torneo no tiene reglamento.");
        _ = reglamento.Puntos ?? throw new InvalidOperationException("Faltan los puntos que el cálculo necesita.");
        var desempate = reglamento.Desempate ?? throw new InvalidOperationException("Falta el desempate que el cálculo necesita.");

        // R-12: Cancelado no cuenta ni como presencia en la fase. R-16: equipos Inscrito con al menos un partido que no esté cancelado.
        var partidos = datos.Partidos
            .Where(p => p.FaseOrden == faseOrden && p.Estado != EstadoDePartidoParaEstadisticas.Cancelado)
            .ToList();
        var inscritos = datos.Equipos.Where(e => e.Inscrito).ToDictionary(e => e.Codigo, StringComparer.Ordinal);
        var equiposDeLaFase = partidos
            .SelectMany(p => new[] { p.LocalCodigo, p.VisitanteCodigo })
            .Where(inscritos.ContainsKey)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        var totales = equiposDeLaFase.ToDictionary(e => e, _ => new Totales(), StringComparer.Ordinal);
        var jugadores = datos.Jugadores.ToDictionary(j => j.Codigo, StringComparer.Ordinal);
        var aportes = new List<AporteDeUnPartido>();
        var tarjetasComputables = 0;
        var tarjetasSinInformar = 0;

        foreach (var partido in partidos)
        {
            var tabla = AporteDePartido.Tabla(partido, reglamento);
            if (tabla is { } t)
            {
                aportes.Add(new AporteDeUnPartido(partido.LocalCodigo, partido.VisitanteCodigo, t.Local, t.Visitante));
                if (totales.TryGetValue(partido.LocalCodigo, out var local)) local.Sumar(t.Local);
                if (totales.TryGetValue(partido.VisitanteCodigo, out var visitante)) visitante.Sumar(t.Visitante);
            }

            var (cuenta, informadas) = AporteDePartido.Tarjetas(partido, reglamento);
            if (!cuenta) continue;

            tarjetasComputables++;
            if (!informadas) tarjetasSinInformar++;
            foreach (var codigo in new[] { partido.LocalCodigo, partido.VisitanteCodigo })
            {
                if (!totales.TryGetValue(codigo, out var equipo)) continue;
                equipo.TarjetasComputables++;
                if (!informadas) equipo.TarjetasSinInformar++;
            }

            // Fair play: solo de los partidos con tarjetas informadas; los demás son datos faltantes, nunca ceros (R-13, R-19).
            // Sin sección fairPlay el criterio no puede estar en la lista (R-06 g): no hay pesos que aplicar.
            if (!informadas || partido.Planilla is null || reglamento.FairPlay is null) continue;
            foreach (var tarjeta in partido.Planilla.Tarjetas)
            {
                if (!jugadores.TryGetValue(tarjeta.JugadorCodigo, out var jugador) || !totales.TryGetValue(jugador.EquipoCodigo, out var dueno)) continue;
                dueno.FairPlay += tarjeta.Cantidad * Peso(reglamento.FairPlay, tarjeta.Color);
            }
        }

        var ordenador = new Ordenador(totales, aportes, desempate);
        var grupos = new List<GrupoFinal>();
        foreach (var porPuntos in equiposDeLaFase.GroupBy(e => totales[e].Puntos).OrderByDescending(g => g.Key))
            grupos.AddRange(ordenador.Resolver([.. porPuntos], 0, null, []));

        var filas = new List<FilaDePosicion>();
        var empates = new List<EmpateSinResolver>();
        var posicion = 1;
        foreach (var grupo in grupos)
        {
            var codigos = grupo.Equipos.Order(StringComparer.Ordinal).ToList();
            foreach (var codigo in codigos)
            {
                var x = totales[codigo];
                filas.Add(new FilaDePosicion(
                    posicion, new EquipoDeEstadisticas(codigo, inscritos[codigo].Nombre),
                    x.Pj, x.Pg, x.Pe, x.Pp, x.Gf, x.Gc, x.Gf - x.Gc, x.PuntosPorResultado, x.Deducciones, x.Puntos,
                    x.TarjetasComputables, x.TarjetasSinInformar));
            }

            if (codigos.Count > 1) empates.Add(new EmpateSinResolver(posicion, codigos, MotivoCriteriosAgotados, grupo.Omitidos));
            posicion += codigos.Count;
        }

        return new ResultadoDePosiciones(filas, empates, new CoberturaDeTarjetas(tarjetasComputables, tarjetasSinInformar));
    }

    private static int Peso(FairPlayParaEstadisticas? pesos, ColorDeTarjetaParaEstadisticas color)
    {
        var p = pesos ?? throw new InvalidOperationException("Faltan los pesos de fair play que el cálculo necesita.");
        return color switch
        {
            ColorDeTarjetaParaEstadisticas.Amarilla => p.Amarilla,
            ColorDeTarjetaParaEstadisticas.Azul => p.Azul,
            _ => p.Roja,
        };
    }

    private static bool EsDirecto(CriterioParaEstadisticas criterio) =>
        criterio is CriterioParaEstadisticas.PuntosDirectos or CriterioParaEstadisticas.DiferenciaDeGolesDirecta
            or CriterioParaEstadisticas.GolesAFavorDirectos;

    private sealed class Totales
    {
        public int Pj, Pg, Pe, Pp, Gf, Gc, PuntosPorResultado, Deducciones, TarjetasComputables, TarjetasSinInformar, FairPlay;

        public int Puntos => PuntosPorResultado - Deducciones;

        public void Sumar(Aporte a)
        {
            Pj += a.Pj;
            Pg += a.Pg;
            Pe += a.Pe;
            Pp += a.Pp;
            Gf += a.Gf;
            Gc += a.Gc;
            PuntosPorResultado += a.PuntosPorResultado;
            Deducciones += a.Deducciones;
        }
    }

    private sealed record AporteDeUnPartido(string Local, string Visitante, Aporte AporteLocal, Aporte AporteVisitante);

    private sealed record GrupoFinal(IReadOnlyList<string> Equipos, IReadOnlyList<CriterioOmitido> Omitidos);

    private sealed class Ordenador(
        Dictionary<string, Totales> totales, List<AporteDeUnPartido> aportes, DesempateParaEstadisticas desempate)
    {
        private readonly IReadOnlyList<CriterioParaEstadisticas> _criterios = desempate.Criterios;

        private int PrimerDirecto()
        {
            for (var i = 0; i < _criterios.Count; i++)
                if (EsDirecto(_criterios[i])) return i;
            return -1;
        }

        /// <summary>
        /// Resuelve un grupo empatado desde el criterio <paramref name="indice"/>. <paramref name="bloque"/> es el grupo que
        /// entró al bloque de criterios directos (R-18). Devuelve los grupos finales en orden; un grupo de dos o más equipos es
        /// un empate sin resolver.
        /// </summary>
        public List<GrupoFinal> Resolver(List<string> grupo, int indice, List<string>? bloque, List<CriterioOmitido> omisiones)
        {
            if (grupo.Count == 1) return [new GrupoFinal(grupo, [])];
            if (indice >= _criterios.Count) return [new GrupoFinal(grupo, Relevantes(omisiones, grupo))];

            var criterio = _criterios[indice];
            if (EsDirecto(criterio))
            {
                // El grupo que entra al bloque es la base de los criterios directos; se conserva mientras el siguiente sea directo.
                var bloqueActual = indice == 0 || !EsDirecto(_criterios[indice - 1]) ? grupo : bloque ?? grupo;
                var partes = Dividir(grupo, e => ValorDirecto(criterio, e, bloqueActual), descendente: true);
                if (partes.Count == 1) return Resolver(grupo, indice + 1, bloqueActual, omisiones);

                var resultado = new List<GrupoFinal>();
                foreach (var parte in partes)
                {
                    if (parte.Count == 1) resultado.Add(new GrupoFinal(parte, []));

                    // Recursivo: cada subgrupo, estrictamente menor, vuelve al primer criterio directo como su propio bloque (R-18).
                    else if (desempate.DirectoRecursivo) resultado.AddRange(Resolver(parte, PrimerDirecto(), parte, omisiones));
                    else resultado.AddRange(Resolver(parte, indice + 1, bloqueActual, omisiones));
                }

                return resultado;
            }

            if (criterio == CriterioParaEstadisticas.FairPlay)
            {
                // R-19: si algún equipo del grupo tiene un partido computable sin informar, el criterio se omite para todo el grupo.
                var sinDatos = grupo.Where(e => totales[e].TarjetasSinInformar > 0).Order(StringComparer.Ordinal).ToList();
                if (sinDatos.Count > 0)
                {
                    List<CriterioOmitido> nuevas =
                    [
                        .. omisiones,
                        new CriterioOmitido(
                            nameof(CriterioParaEstadisticas.FairPlay), CalculadoraDePosiciones.MotivoTarjetasNoInformadas,
                            [.. sinDatos.Select(e => new EquipoSinDatosDeTarjetas(e, totales[e].TarjetasSinInformar))]),
                    ];
                    return Resolver(grupo, indice + 1, bloque, nuevas);
                }
            }

            var general = criterio switch
            {
                CriterioParaEstadisticas.DiferenciaDeGoles => (Func<string, int>)(e => totales[e].Gf - totales[e].Gc),
                CriterioParaEstadisticas.GolesAFavor => e => totales[e].Gf,
                _ => e => totales[e].FairPlay,
            };
            var porGenerales = Dividir(grupo, general, descendente: criterio != CriterioParaEstadisticas.FairPlay);
            if (porGenerales.Count == 1) return Resolver(grupo, indice + 1, bloque, omisiones);

            // Un criterio general que divide manda cada subgrupo al siguiente criterio de la lista, nunca a uno anterior (R-18).
            return [.. porGenerales.SelectMany(parte => Resolver(parte, indice + 1, bloque, omisiones))];
        }

        /// <summary>Parte el grupo por el valor del criterio, en orden (mayor primero, o menor primero para fair play).</summary>
        private static List<List<string>> Dividir(List<string> grupo, Func<string, int> valor, bool descendente)
        {
            var agrupados = grupo.GroupBy(valor);
            var ordenados = descendente ? agrupados.OrderByDescending(g => g.Key) : agrupados.OrderBy(g => g.Key);
            return [.. ordenados.Select(g => g.ToList())];
        }

        /// <summary>R-18: los partidos de la fase, todas las vueltas, entre dos equipos del grupo del bloque.</summary>
        private int ValorDirecto(CriterioParaEstadisticas criterio, string equipo, List<string> bloque)
        {
            var valor = 0;
            foreach (var p in aportes)
            {
                if (!bloque.Contains(p.Local, StringComparer.Ordinal) || !bloque.Contains(p.Visitante, StringComparer.Ordinal)) continue;
                var propio = p.Local == equipo ? p.AporteLocal : p.Visitante == equipo ? p.AporteVisitante : (Aporte?)null;
                if (propio is not { } a) continue;
                valor += criterio switch
                {
                    CriterioParaEstadisticas.PuntosDirectos => a.PuntosPorResultado, // sin deducciones (C-16)
                    CriterioParaEstadisticas.DiferenciaDeGolesDirecta => a.Gf - a.Gc,
                    _ => a.Gf,
                };
            }

            return valor;
        }

        /// <summary>Las omisiones registradas, limitadas a los equipos del grupo que sigue empatado.</summary>
        private static List<CriterioOmitido> Relevantes(List<CriterioOmitido> omisiones, List<string> grupo) =>
        [
            .. omisiones
                .Select(o => o with { Equipos = [.. o.Equipos.Where(e => grupo.Contains(e.Codigo, StringComparer.Ordinal))] })
                .Where(o => o.Equipos.Count > 0),
        ];
    }
}
