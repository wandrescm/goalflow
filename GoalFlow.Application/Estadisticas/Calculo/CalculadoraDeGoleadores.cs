using GoalFlow.Application.Torneos.Contracts;

namespace GoalFlow.Application.Estadisticas.Calculo;

/// <summary>
/// Goleadores de todo el torneo, de todas las fases (Spec 009 R-22, R-23): puro, sin E/S, sin reloj y sin cultura.
/// Solo cuentan las filas <c>Jugador</c>; <c>Autogol</c> y <c>SinAutor</c> nunca cuentan para un jugador (R-15).
/// </summary>
public static class CalculadoraDeGoleadores
{
    public static ResultadoDeGoleadores Calcular(DatosDeTorneoParaEstadisticas datos)
    {
        var reglamento = datos.Reglamento;
        var jugadores = datos.Jugadores.ToDictionary(j => j.Codigo, StringComparer.Ordinal);
        var equipos = datos.Equipos.ToDictionary(e => e.Codigo, StringComparer.Ordinal);
        var goles = new Dictionary<string, int>(StringComparer.Ordinal);

        var computables = 0;
        var conSinAutor = 0;
        var golesTotales = 0;
        var golesDeJugador = 0;
        var autogoles = 0;
        var golesSinAutor = 0;
        var porEquipo = new Dictionary<string, (int Partidos, int ConSinAutor, int GolesSinAutor)>(StringComparer.Ordinal);

        foreach (var partido in datos.Partidos)
        {
            // Un partido sin reglamento solo puede ser Jugado: la configuración de un Adjudicado se exige antes de calcular (R-25).
            var cuenta = partido.Estado switch
            {
                EstadoDePartidoParaEstadisticas.Jugado => partido.Planilla?.MarcadorLocal is not null,
                // Sin marcador deportivo no aporta goles: no necesita configuración (R-25), ni se mira el reglamento.
                EstadoDePartidoParaEstadisticas.Adjudicado => partido.Planilla?.MarcadorLocal is not null
                    && AporteDePartido.CuentaParaGoleadores(partido, reglamento ?? throw new InvalidOperationException("El torneo no tiene reglamento.")),
                _ => false,
            };
            if (!cuenta || partido.Planilla is not { } planilla) continue;

            computables++;
            golesTotales += (planilla.MarcadorLocal ?? 0) + (planilla.MarcadorVisitante ?? 0);
            var sinAutorDelPartido = 0;
            var sinAutorPorEquipo = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var fila in planilla.Goles)
            {
                switch (fila.Tipo)
                {
                    case TipoDeGolParaEstadisticas.Jugador:
                        golesDeJugador += fila.Cantidad;
                        if (fila.JugadorCodigo is { } autor)
                            goles[autor] = (goles.TryGetValue(autor, out var previos) ? previos : 0) + fila.Cantidad;
                        break;
                    case TipoDeGolParaEstadisticas.Autogol:
                        autogoles += fila.Cantidad;
                        break;
                    default:
                        golesSinAutor += fila.Cantidad;
                        sinAutorDelPartido += fila.Cantidad;
                        sinAutorPorEquipo[fila.EquipoAcreditadoCodigo] =
                            (sinAutorPorEquipo.TryGetValue(fila.EquipoAcreditadoCodigo, out var acumulado) ? acumulado : 0) + fila.Cantidad;
                        break;
                }
            }

            if (sinAutorDelPartido > 0) conSinAutor++;
            foreach (var codigo in new[] { partido.LocalCodigo, partido.VisitanteCodigo })
            {
                var actual = porEquipo.TryGetValue(codigo, out var valor) ? valor : (0, 0, 0);
                var propios = sinAutorPorEquipo.TryGetValue(codigo, out var n) ? n : 0;
                porEquipo[codigo] = (actual.Item1 + 1, actual.Item2 + (propios > 0 ? 1 : 0), actual.Item3 + propios);
            }
        }

        // R-22: orden por goles, de mayor a menor; los empates comparten posición y se ordenan por código ordinal.
        var ordenados = goles
            .Where(g => jugadores.ContainsKey(g.Key))
            .OrderByDescending(g => g.Value)
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .ToList();
        var filas = new List<FilaDeGoleador>();
        var posicion = 0;
        var anterior = -1;
        for (var i = 0; i < ordenados.Count; i++)
        {
            if (ordenados[i].Value != anterior) posicion = i + 1;
            anterior = ordenados[i].Value;
            var jugador = jugadores[ordenados[i].Key];
            var equipo = equipos[jugador.EquipoCodigo];
            filas.Add(new FilaDeGoleador(
                posicion, new JugadorDeEstadisticas(jugador.Codigo, jugador.Nombre),
                new EquipoDeEstadisticas(equipo.Codigo, equipo.Nombre), ordenados[i].Value));
        }

        var cobertura = new CoberturaDeGoleadores(
            computables, conSinAutor, golesTotales, golesDeJugador, autogoles, golesSinAutor, golesDeJugador + autogoles,
            [.. porEquipo
                .OrderBy(e => e.Key, StringComparer.Ordinal)
                .Select(e => new CoberturaDeGoleadoresPorEquipo(
                    new EquipoDeEstadisticas(e.Key, equipos[e.Key].Nombre), e.Value.Partidos, e.Value.ConSinAutor, e.Value.GolesSinAutor))]);
        return new ResultadoDeGoleadores(filas, cobertura);
    }
}
