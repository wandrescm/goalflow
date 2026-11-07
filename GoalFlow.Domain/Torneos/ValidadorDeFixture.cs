namespace GoalFlow.Domain.Torneos;

/// <summary>Códigos de error del fixture (Spec 005 R-13, R-16, R-17). Estables y sin datos del usuario.</summary>
public static class CodigosDeFixture
{
    public const string CodigoDeEquipoDuplicado = "codigo_de_equipo_duplicado";
    public const string CodigoDeJugadorDuplicado = "codigo_de_jugador_duplicado";
    public const string OrdenDeFaseDuplicado = "orden_de_fase_duplicado";
    public const string FaseInexistente = "fase_inexistente";
    public const string EquipoInexistente = "equipo_inexistente";
    public const string ParejaRepetida = "pareja_repetida";
    public const string EquipoRepetidoEnJornada = "equipo_repetido_en_jornada";
    public const string SinFases = "sin_fases";
    public const string EquiposInsuficientes = "equipos_insuficientes";
    public const string PartidoDeEquipoRetiradoSinCancelar = "partido_de_equipo_retirado_sin_cancelar";
    public const string TipoDeFaseInvalido = "tipo_de_fase_invalido";
    public const string DemasiadasFases = "demasiadas_fases";
    public const string DemasiadosEquipos = "demasiados_equipos";
    public const string DemasiadosJugadores = "demasiados_jugadores";
    public const string DemasiadosPartidos = "demasiados_partidos";
}

/// <summary>Valida un fixture completo y devuelve todos los errores, no solo el primero (Spec 005 R-13).</summary>
public static class ValidadorDeFixture
{
    public const int MaximoDeFases = 8;
    public const int MaximoDeEquipos = 64;
    public const int MaximoDeJugadoresPorEquipo = 60;
    public const int MaximoDePartidos = 1000;

    public static IReadOnlyList<ErrorDeFixture> Validar(FixtureDescripcion fixture)
    {
        var errores = new List<ErrorDeFixture>();
        void Agregar(string ruta, string codigo) => errores.Add(new ErrorDeFixture(ruta, codigo));

        ValidarLimites(fixture, Agregar);

        // R-12 sobre cada dato, con su ruta.
        if (Reglas.NombreNormalizado(fixture.Nombre) is null) Agregar("nombre", RazonesDeDominio.NombreInvalido);

        for (var i = 0; i < fixture.Fases.Count; i++)
        {
            var fase = fixture.Fases[i];
            if (fase.Orden < 1) Agregar($"fases[{i}].orden", RazonesDeDominio.OrdenInvalido);
            if (fase.Vueltas is not (1 or 2)) Agregar($"fases[{i}].vueltas", RazonesDeDominio.VueltasInvalidas);
            if (!Enum.GetNames<TipoDeFase>().Contains(fase.Tipo, StringComparer.Ordinal))
                Agregar($"fases[{i}].tipo", CodigosDeFixture.TipoDeFaseInvalido);
        }

        for (var i = 0; i < fixture.Equipos.Count; i++)
        {
            var equipo = fixture.Equipos[i];
            if (!Reglas.CodigoValido(equipo.Codigo)) Agregar($"equipos[{i}].codigo", RazonesDeDominio.CodigoInvalido);
            if (Reglas.NombreNormalizado(equipo.Nombre) is null) Agregar($"equipos[{i}].nombre", RazonesDeDominio.NombreInvalido);

            for (var j = 0; j < equipo.Jugadores.Count; j++)
            {
                var jugador = equipo.Jugadores[j];
                if (!Reglas.CodigoValido(jugador.Codigo))
                    Agregar($"equipos[{i}].jugadores[{j}].codigo", RazonesDeDominio.CodigoInvalido);
                if (Reglas.NombreNormalizado(jugador.Nombre) is null)
                    Agregar($"equipos[{i}].jugadores[{j}].nombre", RazonesDeDominio.NombreInvalido);
            }
        }

        // (a) códigos de equipo únicos, (b) códigos de jugador únicos en todo el payload, (c) órdenes de fase únicos.
        var equiposPorCodigo = new Dictionary<string, EquipoDescripcion>(StringComparer.Ordinal);
        for (var i = 0; i < fixture.Equipos.Count; i++)
        {
            var equipo = fixture.Equipos[i];
            
            if (!equiposPorCodigo.TryAdd(equipo.Codigo, equipo))
                Agregar($"equipos[{i}].codigo", CodigosDeFixture.CodigoDeEquipoDuplicado);
        }

        var jugadoresVistos = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < fixture.Equipos.Count; i++)
        for (var j = 0; j < fixture.Equipos[i].Jugadores.Count; j++)
        {
            var codigo = fixture.Equipos[i].Jugadores[j].Codigo;
            if (!jugadoresVistos.Add(codigo))
                Agregar($"equipos[{i}].jugadores[{j}].codigo", CodigosDeFixture.CodigoDeJugadorDuplicado);
        }

        var fasesPorOrden = new Dictionary<int, FaseDescripcion>();
        for (var i = 0; i < fixture.Fases.Count; i++)
        {
            var fase = fixture.Fases[i];
            if (fase.Orden >= 1 && !fasesPorOrden.TryAdd(fase.Orden, fase))
                Agregar($"fases[{i}].orden", CodigosDeFixture.OrdenDeFaseDuplicado);
        }

        // (g) al menos una fase y dos equipos.
        if (fixture.Fases.Count == 0) Agregar("fases", CodigosDeFixture.SinFases);
        if (fixture.Equipos.Count < 2) Agregar("equipos", CodigosDeFixture.EquiposInsuficientes);

        ValidarPartidos(fixture, fasesPorOrden, equiposPorCodigo, Agregar);
        return errores;
    }

    private static void ValidarLimites(FixtureDescripcion fixture, Action<string, string> agregar)
    {
        if (fixture.Fases.Count > MaximoDeFases) agregar("fases", CodigosDeFixture.DemasiadasFases);
        if (fixture.Equipos.Count > MaximoDeEquipos) agregar("equipos", CodigosDeFixture.DemasiadosEquipos);
        if (fixture.Equipos.Any(e => e.Jugadores.Count > MaximoDeJugadoresPorEquipo))
            agregar("jugadores", CodigosDeFixture.DemasiadosJugadores);
        if (fixture.Partidos.Count > MaximoDePartidos) agregar("partidos", CodigosDeFixture.DemasiadosPartidos);
    }

    private static void ValidarPartidos(
        FixtureDescripcion fixture,
        Dictionary<int, FaseDescripcion> fasesPorOrden,
        Dictionary<string, EquipoDescripcion> equiposPorCodigo,
        Action<string, string> agregar)
    {
        // (e) parejas ya vistas por fase; (f) equipos que ya juegan por (fase, jornada), sin contar los cancelados.
        var parejas = new HashSet<(int Fase, string A, string B)>();
        var ocupados = new HashSet<(int Fase, int Jornada, string Equipo)>();

        for (var i = 0; i < fixture.Partidos.Count; i++)
        {
            var partido = fixture.Partidos[i];

            if (partido.Jornada < 1) agregar($"partidos[{i}].jornada", RazonesDeDominio.JornadaInvalida);
            if (partido.ProgramadoEn is { } hora && HoraProgramada.Interpretar(hora, out _) is { } errorDeHora)
                agregar($"partidos[{i}].programadoEn", errorDeHora);

            // (d) fase y equipos existentes.
            var faseExiste = fasesPorOrden.TryGetValue(partido.Fase, out var fase);
            if (!faseExiste) agregar($"partidos[{i}].fase", CodigosDeFixture.FaseInexistente);

            var localExiste = equiposPorCodigo.ContainsKey(partido.Local);
            var visitanteExiste = equiposPorCodigo.ContainsKey(partido.Visitante);
            if (!localExiste) agregar($"partidos[{i}].local", CodigosDeFixture.EquipoInexistente);
            if (!visitanteExiste) agregar($"partidos[{i}].visitante", CodigosDeFixture.EquipoInexistente);

            var mismoEquipo = partido.Local == partido.Visitante;
            if (mismoEquipo) agregar($"partidos[{i}].visitante", RazonesDeDominio.LocalIgualAVisitante);

            // (h) todo partido de un equipo retirado debe ir cancelado.
            if (!partido.Cancelado
                && ((localExiste && equiposPorCodigo[partido.Local].Retirado)
                    || (visitanteExiste && equiposPorCodigo[partido.Visitante].Retirado)))
                agregar($"partidos[{i}].cancelado", CodigosDeFixture.PartidoDeEquipoRetiradoSinCancelar);

            if (!faseExiste || !localExiste || !visitanteExiste || mismoEquipo) continue;

            // (e) con 1 vuelta la pareja no tiene orden; con 2, sí. Un partido cancelado conserva la identidad de su pareja.
            if (fase!.Vueltas is 1 or 2)
            {
                var (a, b) = fase.Vueltas == 1 && string.CompareOrdinal(partido.Local, partido.Visitante) > 0
                    ? (partido.Visitante, partido.Local)
                    : (partido.Local, partido.Visitante);
                if (!parejas.Add((partido.Fase, a, b)))
                    agregar($"partidos[{i}]", CodigosDeFixture.ParejaRepetida);
            }

            // (f) un equipo juega como mucho una vez por (fase, jornada); el cancelado libera su turno.
            if (partido.Cancelado) continue;
            var yaJuega = ocupados.Contains((partido.Fase, partido.Jornada, partido.Local))
                || ocupados.Contains((partido.Fase, partido.Jornada, partido.Visitante));
            if (yaJuega) agregar($"partidos[{i}]", CodigosDeFixture.EquipoRepetidoEnJornada);
            ocupados.Add((partido.Fase, partido.Jornada, partido.Local));
            ocupados.Add((partido.Fase, partido.Jornada, partido.Visitante));
        }
    }
}
