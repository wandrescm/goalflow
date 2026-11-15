using GoalFlow.Application.Torneos.Contracts;

namespace GoalFlow.Application.Estadisticas.Calculo;

/// <summary>
/// Valla de todo el torneo, de todas las fases (Spec 009 R-24): puro, sin E/S, sin reloj y sin cultura. Es la suma de goles en
/// contra, nunca un promedio, y no declara ganador.
/// </summary>
public static class CalculadoraDeValla
{
    public static ResultadoDeValla Calcular(DatosDeTorneoParaEstadisticas datos)
    {
        var inscritos = datos.Equipos.Where(e => e.Inscrito).ToDictionary(e => e.Codigo, StringComparer.Ordinal);
        var porEquipo = new Dictionary<string, (int Partidos, int Goles)>(StringComparer.Ordinal);

        foreach (var partido in datos.Partidos)
        {
            if (partido.Estado is not (EstadoDePartidoParaEstadisticas.Jugado or EstadoDePartidoParaEstadisticas.Adjudicado)) continue;

            // Un Adjudicado necesita su configuración (R-25); un torneo sin reglamento solo puede tener partidos Jugado.
            var enContra = partido.Estado == EstadoDePartidoParaEstadisticas.Adjudicado
                ? AporteDePartido.Valla(partido, datos.Reglamento ?? throw new InvalidOperationException("El torneo no tiene reglamento."))
                : AporteDePartido.Valla(partido, new ReglamentoParaEstadisticas(0, string.Empty, null, null, null, null));
            if (enContra is not { } contra) continue;

            foreach (var (codigo, goles) in new[] { (partido.LocalCodigo, contra.Local), (partido.VisitanteCodigo, contra.Visitante) })
            {
                if (!inscritos.ContainsKey(codigo)) continue;
                var actual = porEquipo.TryGetValue(codigo, out var valor) ? valor : (0, 0);
                porEquipo[codigo] = (actual.Item1 + 1, actual.Item2 + goles);
            }
        }

        var ordenados = porEquipo
            .OrderBy(e => e.Value.Goles)
            .ThenBy(e => e.Key, StringComparer.Ordinal)
            .ToList();
        var filas = new List<FilaDeValla>();
        var posicion = 0;
        var anterior = -1;
        for (var i = 0; i < ordenados.Count; i++)
        {
            if (ordenados[i].Value.Goles != anterior) posicion = i + 1;
            anterior = ordenados[i].Value.Goles;
            filas.Add(new FilaDeValla(
                posicion, new EquipoDeEstadisticas(ordenados[i].Key, inscritos[ordenados[i].Key].Nombre),
                ordenados[i].Value.Partidos, ordenados[i].Value.Goles));
        }

        return new ResultadoDeValla(filas);
    }
}
