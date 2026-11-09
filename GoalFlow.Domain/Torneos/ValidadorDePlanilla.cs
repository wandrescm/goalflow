namespace GoalFlow.Domain.Torneos;

// Descripción de una planilla tal como llega en el PUT (Spec 006 R-09): valores primitivos, que la capa de entrada traduce y
// el validador revisa sin tocar la base. Los jugadores se refieren por su código de liga.

public sealed record MarcadorDescripcion(int? Local, int? Visitante);

public sealed record GolDeJugadorDescripcion(string Jugador, int Cantidad);

public sealed record TarjetaDescripcion(string Jugador, string Color, int Cantidad);

public sealed record PlanillaDescripcion(
    string Estado,
    MarcadorDescripcion? Marcador,
    MarcadorDescripcion? Oficial,
    string? Motivo,
    string? MotivoRectificacion,
    bool? TarjetasInformadas,
    IReadOnlyList<GolDeJugadorDescripcion> Goles,
    IReadOnlyList<GolDeJugadorDescripcion> Autogoles,
    MarcadorDescripcion GolesSinAutor,
    IReadOnlyList<TarjetaDescripcion> Tarjetas);

public enum LadoDelPartido
{
    Local,
    Visitante,
}

/// <param name="Ruta">Ruta en la petición, por ejemplo <c>tarjetas[2].jugador</c>.</param>
/// <param name="Codigo">Código estable del error; nunca contiene datos del usuario (Spec 006 R-13).</param>
public sealed record ErrorDePlanilla(string Ruta, string Codigo);

/// <summary>Fila de goles ya resuelta: el autor se identifica por su código y el gol se acredita a un lado (R-03, C-02).</summary>
public sealed record FilaDeGol(TipoDeGol Tipo, LadoDelPartido Acreditado, string? Jugador, int Cantidad);

public sealed record FilaDeTarjeta(string Jugador, ColorDeTarjeta Color, int Cantidad);

/// <summary>
/// La planilla ya validada y resuelta: lo que se guarda y lo que se compara (R-17). Sin cantidades en cero y sin orden:
/// las filas son conjuntos, y un miembro ausente equivale a uno vacío.
/// </summary>
public sealed record PlanillaResuelta(
    EstadoDePartido Estado,
    int? MarcadorLocal,
    int? MarcadorVisitante,
    int? OficialLocal,
    int? OficialVisitante,
    MotivoDeResultado? Motivo,
    MotivoDeRectificacion? MotivoRectificacion,
    bool? TarjetasInformadas,
    IReadOnlyList<FilaDeGol> Goles,
    IReadOnlyList<FilaDeTarjeta> Tarjetas)
{
    /// <summary>Igualdad normalizada de R-17: estado, ambos marcadores, ambos motivos, la bandera y los conjuntos de filas.</summary>
    public bool Equivale(PlanillaResuelta otra) =>
        MismoContenidoSinMotivoDeRectificacion(otra) && MotivoRectificacion == otra.MotivoRectificacion;

    /// <summary>Igual que <see cref="Equivale"/> pero sin mirar el motivo de rectificación (R-14: un motivo no se edita solo).</summary>
    public bool MismoContenidoSinMotivoDeRectificacion(PlanillaResuelta otra) =>
        Estado == otra.Estado
        && MarcadorLocal == otra.MarcadorLocal && MarcadorVisitante == otra.MarcadorVisitante
        && OficialLocal == otra.OficialLocal && OficialVisitante == otra.OficialVisitante
        && Motivo == otra.Motivo
        && TarjetasInformadas == otra.TarjetasInformadas
        && Goles.ToHashSet().SetEquals(otra.Goles)
        && Tarjetas.ToHashSet().SetEquals(otra.Tarjetas);
}

/// <param name="Planilla">La planilla resuelta, o <c>null</c> si hay errores.</param>
public sealed record ResultadoDeValidacionDePlanilla(IReadOnlyList<ErrorDePlanilla> Errores, PlanillaResuelta? Planilla);

public static class CodigosDePlanilla
{
    public const string EstadoInvalido = "estado_invalido";
    public const string MarcadorRequerido = "marcador_requerido";
    public const string MarcadorNoPermitido = "marcador_no_permitido";
    public const string MarcadorIncompleto = "marcador_incompleto";
    public const string MarcadorFueraDeRango = "marcador_fuera_de_rango";
    public const string OficialRequerido = "oficial_requerido";
    public const string OficialNoPermitido = "oficial_no_permitido";
    public const string OficialIncompleto = "oficial_incompleto";
    public const string OficialFueraDeRango = "oficial_fuera_de_rango";
    public const string MotivoRequerido = "motivo_requerido";
    public const string MotivoNoPermitido = "motivo_no_permitido";
    public const string MotivoInvalido = "motivo_invalido";
    public const string MotivoDeRectificacionInvalido = "motivo_de_rectificacion_invalido";
    public const string ContenidoSinMarcador = "contenido_sin_marcador";
    public const string ContenidoNoPermitido = "contenido_no_permitido";
    public const string TarjetasInformadasRequerido = "tarjetas_informadas_requerido";
    public const string TarjetasInformadasNoPermitido = "tarjetas_informadas_no_permitido";
    public const string TarjetasSinInformar = "tarjetas_sin_informar";
    public const string JugadorFueraDelPartido = "jugador_fuera_del_partido";
    public const string JugadorRepetido = "jugador_repetido";
    public const string CantidadInvalida = "cantidad_invalida";
    public const string ColorInvalido = "color_invalido";
    public const string SumaDeGolesDistintaDelMarcador = "suma_de_goles_distinta_del_marcador";

    /// <summary>R-14: el código del 422 de la regla de rectificación.</summary>
    public const string RectificacionInvalida = "rectificacion_invalida";
}

/// <summary>
/// Validador puro de la planilla (Spec 006 R-11 y R-12): sin base de datos. Devuelve todos los errores, no solo el primero,
/// y la planilla resuelta cuando no hay ninguno.
/// </summary>
public static class ValidadorDePlanilla
{
    /// <param name="jugadoresDelPartido">Código de cada jugador inscrito en el torneo con el equipo local o visitante, y su lado (R-12 f).</param>
    public static ResultadoDeValidacionDePlanilla Validar(
        PlanillaDescripcion planilla, IReadOnlyDictionary<string, LadoDelPartido> jugadoresDelPartido)
    {
        var errores = new List<ErrorDePlanilla>();
        void Error(string ruta, string codigo) => errores.Add(new ErrorDePlanilla(ruta, codigo));

        var estado = EstadoDe(planilla.Estado);
        if (estado is null) Error("estado", CodigosDePlanilla.EstadoInvalido);

        var marcador = Marcador(planilla.Marcador, "marcador", CodigosDePlanilla.MarcadorIncompleto, CodigosDePlanilla.MarcadorFueraDeRango, Error);
        var oficial = Marcador(planilla.Oficial, "oficial", CodigosDePlanilla.OficialIncompleto, CodigosDePlanilla.OficialFueraDeRango, Error);
        var tieneMarcador = planilla.Marcador is not null;
        var tieneOficial = planilla.Oficial is not null;

        MotivoDeRectificacion? rectificacion = null;
        if (planilla.MotivoRectificacion is { } textoDeRectificacion)
        {
            if (Enum.TryParse(textoDeRectificacion, ignoreCase: false, out MotivoDeRectificacion r) && Enum.IsDefined(r)) rectificacion = r;
            else Error("motivoRectificacion", CodigosDePlanilla.MotivoDeRectificacionInvalido);
        }

        var motivo = ReglasDelEstado(estado, planilla, tieneMarcador, tieneOficial, Error);

        var contenidoPermitido = estado is not null && estado != EstadoDePartido.Programado && tieneMarcador;
        var goles = new List<FilaDeGol>();
        var tarjetas = new List<FilaDeTarjeta>();
        var golesLimpios = true;

        if (contenidoPermitido)
        {
            golesLimpios = ResolverGoles(planilla, jugadoresDelPartido, goles, Error);
            ResolverTarjetas(planilla, jugadoresDelPartido, tarjetas, Error);

            // (j): la bandera es obligatoria con marcador, y con ella en false no puede haber tarjetas.
            if (planilla.TarjetasInformadas is null) Error("tarjetasInformadas", CodigosDePlanilla.TarjetasInformadasRequerido);
            else if (planilla.TarjetasInformadas == false && planilla.Tarjetas.Count > 0)
                Error("tarjetas", CodigosDePlanilla.TarjetasSinInformar);

            // (g): solo si cada gol se pudo resolver; si no, el error de la fila ya lo explica.
            if (golesLimpios && marcador is { } m) ComprobarSuma(m, goles, Error);
        }
        else if (estado is not null)
        {
            var codigo = estado == EstadoDePartido.Programado ? CodigosDePlanilla.ContenidoNoPermitido : CodigosDePlanilla.ContenidoSinMarcador;
            MarcarContenido(planilla, codigo, Error);
            if (planilla.TarjetasInformadas is not null)
                Error("tarjetasInformadas", CodigosDePlanilla.TarjetasInformadasNoPermitido);
        }

        if (errores.Count > 0 || estado is null) return new ResultadoDeValidacionDePlanilla(errores, null);

        return new ResultadoDeValidacionDePlanilla(errores, new PlanillaResuelta(
            estado.Value, marcador?.Local, marcador?.Visitante, oficial?.Local, oficial?.Visitante,
            motivo, rectificacion, contenidoPermitido ? planilla.TarjetasInformadas : null, goles, tarjetas));
    }

    private static readonly EstadoDePartido[] EstadosDeUnaPlanilla =
        [EstadoDePartido.Programado, EstadoDePartido.Jugado, EstadoDePartido.Suspendido, EstadoDePartido.Adjudicado];

    private static EstadoDePartido? EstadoDe(string texto) =>
        Enum.TryParse(texto, ignoreCase: false, out EstadoDePartido e) && EstadosDeUnaPlanilla.Contains(e) ? e : null;

    private static (int Local, int Visitante)? Marcador(
        MarcadorDescripcion? marcador, string ruta, string incompleto, string fueraDeRango, Action<string, string> error)
    {
        if (marcador is null) return null;
        var valido = true;
        if (marcador.Local is null) { error($"{ruta}.local", incompleto); valido = false; }
        else if (marcador.Local is < 0 or > 99) { error($"{ruta}.local", fueraDeRango); valido = false; }
        if (marcador.Visitante is null) { error($"{ruta}.visitante", incompleto); valido = false; }
        else if (marcador.Visitante is < 0 or > 99) { error($"{ruta}.visitante", fueraDeRango); valido = false; }
        return valido ? (marcador.Local!.Value, marcador.Visitante!.Value) : null;
    }

    /// <summary>R-11 (a) a (d): marcador, marcador oficial y motivo según el estado. Devuelve el motivo deportivo.</summary>
    private static MotivoDeResultado? ReglasDelEstado(
        EstadoDePartido? estado, PlanillaDescripcion planilla, bool tieneMarcador, bool tieneOficial, Action<string, string> error)
    {
        switch (estado)
        {
            case EstadoDePartido.Jugado:
                if (!tieneMarcador) error("marcador", CodigosDePlanilla.MarcadorRequerido);
                if (tieneOficial) error("oficial", CodigosDePlanilla.OficialNoPermitido);
                if (planilla.Motivo is not null) error("motivo", CodigosDePlanilla.MotivoNoPermitido);
                return null;
            case EstadoDePartido.Suspendido:
                if (!tieneMarcador) error("marcador", CodigosDePlanilla.MarcadorRequerido);
                if (tieneOficial) error("oficial", CodigosDePlanilla.OficialNoPermitido);
                return MotivoDe(planilla.Motivo, [MotivoDeResultado.Clima, MotivoDeResultado.Luz, MotivoDeResultado.Disturbios, MotivoDeResultado.Otro], error);
            case EstadoDePartido.Adjudicado:
                if (!tieneOficial) error("oficial", CodigosDePlanilla.OficialRequerido);
                return MotivoDe(planilla.Motivo, [MotivoDeResultado.WalkOver, MotivoDeResultado.Sancion, MotivoDeResultado.Otro], error);
            case EstadoDePartido.Programado:
                if (tieneMarcador) error("marcador", CodigosDePlanilla.MarcadorNoPermitido);
                if (tieneOficial) error("oficial", CodigosDePlanilla.OficialNoPermitido);
                if (planilla.Motivo is not null) error("motivo", CodigosDePlanilla.MotivoNoPermitido);
                return null;
            default:
                return null;
        }
    }

    private static MotivoDeResultado? MotivoDe(string? texto, MotivoDeResultado[] permitidos, Action<string, string> error)
    {
        if (texto is null)
        {
            error("motivo", CodigosDePlanilla.MotivoRequerido);
            return null;
        }

        if (Enum.TryParse(texto, ignoreCase: false, out MotivoDeResultado motivo) && permitidos.Contains(motivo)) return motivo;
        error("motivo", CodigosDePlanilla.MotivoInvalido);
        return null;
    }

    /// <summary>Un error por fila de contenido que no puede existir: R-11 (d) y (e).</summary>
    private static void MarcarContenido(PlanillaDescripcion planilla, string codigo, Action<string, string> error)
    {
        for (var i = 0; i < planilla.Goles.Count; i++) error($"goles[{i}]", codigo);
        for (var i = 0; i < planilla.Autogoles.Count; i++) error($"autogoles[{i}]", codigo);
        if ((planilla.GolesSinAutor.Local ?? 0) != 0 || (planilla.GolesSinAutor.Visitante ?? 0) != 0) error("golesSinAutor", codigo);
        for (var i = 0; i < planilla.Tarjetas.Count; i++) error($"tarjetas[{i}]", codigo);
    }

    /// <returns><c>false</c> si algún gol no se pudo resolver o tiene una cantidad inválida.</returns>
    private static bool ResolverGoles(
        PlanillaDescripcion planilla, IReadOnlyDictionary<string, LadoDelPartido> jugadores, List<FilaDeGol> filas, Action<string, string> error)
    {
        var limpio = true;

        void Lista(IReadOnlyList<GolDeJugadorDescripcion> lista, string nombre, TipoDeGol tipo)
        {
            var vistos = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < lista.Count; i++)
            {
                var gol = lista[i];
                var resuelto = jugadores.TryGetValue(gol.Jugador ?? string.Empty, out var lado);
                if (!resuelto) { error($"{nombre}[{i}].jugador", CodigosDePlanilla.JugadorFueraDelPartido); limpio = false; }
                else if (!vistos.Add(gol.Jugador!)) { error($"{nombre}[{i}].jugador", CodigosDePlanilla.JugadorRepetido); limpio = false; }

                if (gol.Cantidad < 1) { error($"{nombre}[{i}].cantidad", CodigosDePlanilla.CantidadInvalida); limpio = false; }
                if (!resuelto || gol.Cantidad < 1) continue;

                // C-02: el autogol se acredita al rival del autor y nunca cuenta para el autor.
                var acreditado = tipo == TipoDeGol.Autogol ? Opuesto(lado) : lado;
                filas.Add(new FilaDeGol(tipo, acreditado, gol.Jugador, gol.Cantidad));
            }
        }

        Lista(planilla.Goles, "goles", TipoDeGol.Jugador);
        Lista(planilla.Autogoles, "autogoles", TipoDeGol.Autogol);

        foreach (var (lado, cantidad, ruta) in new[]
        {
            (LadoDelPartido.Local, planilla.GolesSinAutor.Local ?? 0, "golesSinAutor.local"),
            (LadoDelPartido.Visitante, planilla.GolesSinAutor.Visitante ?? 0, "golesSinAutor.visitante"),
        })
        {
            if (cantidad < 0) { error(ruta, CodigosDePlanilla.CantidadInvalida); limpio = false; }
            else if (cantidad > 0) filas.Add(new FilaDeGol(TipoDeGol.SinAutor, lado, null, cantidad));
        }

        return limpio;
    }

    private static void ResolverTarjetas(
        PlanillaDescripcion planilla, IReadOnlyDictionary<string, LadoDelPartido> jugadores, List<FilaDeTarjeta> filas, Action<string, string> error)
    {
        var vistas = new HashSet<(string, ColorDeTarjeta)>();
        for (var i = 0; i < planilla.Tarjetas.Count; i++)
        {
            var tarjeta = planilla.Tarjetas[i];
            var colorValido = Enum.TryParse(tarjeta.Color, ignoreCase: false, out ColorDeTarjeta color) && Enum.IsDefined(color);
            var jugadorValido = jugadores.ContainsKey(tarjeta.Jugador ?? string.Empty);
            var cantidadValida = tarjeta.Cantidad is >= 1 and <= 9;

            if (!jugadorValido) error($"tarjetas[{i}].jugador", CodigosDePlanilla.JugadorFueraDelPartido);
            else if (colorValido && !vistas.Add((tarjeta.Jugador!, color))) error($"tarjetas[{i}].jugador", CodigosDePlanilla.JugadorRepetido);
            if (!colorValido) error($"tarjetas[{i}].color", CodigosDePlanilla.ColorInvalido);
            if (!cantidadValida) error($"tarjetas[{i}].cantidad", CodigosDePlanilla.CantidadInvalida);

            if (jugadorValido && colorValido && cantidadValida) filas.Add(new FilaDeTarjeta(tarjeta.Jugador!, color, tarjeta.Cantidad));
        }
    }

    /// <summary>R-12 (g): goles de jugadores del lado, más autogoles del rival, más goles sin autor, igualan el marcador.</summary>
    private static void ComprobarSuma((int Local, int Visitante) marcador, List<FilaDeGol> goles, Action<string, string> error)
    {
        long Suma(LadoDelPartido lado) => goles.Where(g => g.Acreditado == lado).Sum(g => (long)g.Cantidad);

        if (Suma(LadoDelPartido.Local) != marcador.Local)
            error("marcador.local", CodigosDePlanilla.SumaDeGolesDistintaDelMarcador);
        if (Suma(LadoDelPartido.Visitante) != marcador.Visitante)
            error("marcador.visitante", CodigosDePlanilla.SumaDeGolesDistintaDelMarcador);
    }

    private static LadoDelPartido Opuesto(LadoDelPartido lado) =>
        lado == LadoDelPartido.Local ? LadoDelPartido.Visitante : LadoDelPartido.Local;
}

/// <summary>
/// Regla de rectificación (Spec 006 R-14). Se evalúa solo después de R-16 y R-18: nunca convierte un reenvío idéntico ni una
/// petición desfasada en un 422.
/// </summary>
public static class ReglaDeRectificacion
{
    /// <param name="estadoActual">El estado del partido: <c>Programado</c> si no hay planilla (versión 0).</param>
    /// <param name="vigente">La planilla vigente, o <c>null</c> en la versión 0.</param>
    /// <param name="deseada">La planilla que se pide, ya validada.</param>
    /// <returns><c>true</c> si el cambio es válido.</returns>
    public static bool EsValida(EstadoDePartido estadoActual, PlanillaResuelta? vigente, PlanillaResuelta deseada)
    {
        // Sin planilla (versión 0) el contenido vigente es el de una planilla Programado vacía.
        var actual = vigente ?? new PlanillaResuelta(estadoActual, null, null, null, null, null, null, null, [], []);

        // Un reenvío idéntico no llega aquí (R-17); si llegara, no cambia nada y no hay nada que rechazar.
        if (actual.Equivale(deseada)) return true;

        // Un motivo nunca se edita solo: agregado, quitado o cambiado sin tocar nada más es inválido (R-14, C-27).
        if (actual.MismoContenidoSinMotivoDeRectificacion(deseada)) return false;

        var desde = estadoActual;
        var hacia = deseada.Estado;
        if (desde == EstadoDePartido.Programado && hacia == EstadoDePartido.Programado) return false;

        var conMotivo = deseada.MotivoRectificacion is not null;
        return EsExcepcional(desde, hacia) ? conMotivo : !conMotivo;
    }

    /// <summary>
    /// Excepcionales: entre estados de resultado distintos del primer registro y de completar o resolver una suspensión, y el
    /// retiro de la planilla. Todo lo demás es ordinario: el primer registro, completar o resolver una suspensión, y corregir
    /// dentro del mismo estado.
    /// </summary>
    private static bool EsExcepcional(EstadoDePartido desde, EstadoDePartido hacia) =>
        (desde, hacia) switch
        {
            (EstadoDePartido.Jugado, EstadoDePartido.Suspendido or EstadoDePartido.Adjudicado) => true,
            (EstadoDePartido.Adjudicado, EstadoDePartido.Jugado or EstadoDePartido.Suspendido) => true,
            (EstadoDePartido.Jugado or EstadoDePartido.Suspendido or EstadoDePartido.Adjudicado, EstadoDePartido.Programado) => true,
            _ => false,
        };
}
