namespace GoalFlow.Domain.Torneos;

// El reglamento de un torneo (Spec 009 R-03). Dos formas: la descripción tal como llega en la importación (miembros
// anulables: un miembro ausente es un error, nunca un valor por defecto) y el contenido canónico ya validado.

public enum EstadoDeReglamento
{
    Provisional,
    Confirmado,
}

/// <summary>Lista cerrada de criterios de desempate (R-04). Los tres primeros son directos; los otros, generales.</summary>
public enum CriterioDeDesempate
{
    PuntosDirectos,
    DiferenciaDeGolesDirecta,
    GolesAFavorDirectos,
    DiferenciaDeGoles,
    GolesAFavor,
    FairPlay,
}

// ---- Descripción (entrada) ----

public sealed record PuntosDescripcion(int? Victoria, int? Empate, int? Derrota);

public sealed record DesempateDescripcion(IReadOnlyList<string>? Criterios, bool? DirectoRecursivo);

public sealed record FairPlayDescripcion(int? Amarilla, int? Azul, int? Roja);

public sealed record AdjudicacionDeMotivoDescripcion(
    int? PuntosGanador, int? PuntosPerdedor, int? PuntosEmpate, int? DeduccionPerdedor, int? DeduccionEmpate,
    bool? CuentaComoJugado, bool? GolesOficialesEnTabla, bool? GolesOficialesEnValla,
    bool? GolesDeportivosEnGoleadores, bool? TarjetasEnFairPlay);

/// <summary>Las llaves son los motivos de adjudicación de Spec 006 R-11 c; una llave ausente es un motivo no decidido.</summary>
public sealed record AdjudicacionDescripcion(
    AdjudicacionDeMotivoDescripcion? WalkOver, AdjudicacionDeMotivoDescripcion? Sancion, AdjudicacionDeMotivoDescripcion? Otro);

/// <param name="Estado">Texto de <c>estado</c>; el validador comprueba que sea <c>Provisional</c> o <c>Confirmado</c>.</param>
public sealed record ReglamentoDescripcion(
    string? Estado, PuntosDescripcion? Puntos, DesempateDescripcion? Desempate, FairPlayDescripcion? FairPlay,
    AdjudicacionDescripcion? Adjudicacion);

/// <param name="Ruta">Ruta en la petición, por ejemplo <c>reglamento.puntos.victoria</c>.</param>
/// <param name="Codigo">Código estable del error; nunca contiene datos del usuario.</param>
public sealed record ErrorDeReglamento(string Ruta, string Codigo);

// ---- Contenido canónico (validado) ----

public sealed record PuntosDeReglamento(int Victoria, int Empate, int Derrota);

public sealed record FairPlayDeReglamento(int Amarilla, int Azul, int Roja);

public sealed record AdjudicacionDeMotivo(
    int PuntosGanador, int PuntosPerdedor, int PuntosEmpate, int DeduccionPerdedor, int DeduccionEmpate,
    bool CuentaComoJugado, bool GolesOficialesEnTabla, bool GolesOficialesEnValla,
    bool GolesDeportivosEnGoleadores, bool TarjetasEnFairPlay);

/// <summary>El orden de los criterios importa (R-07 c): la igualdad de este record compara la lista en orden.</summary>
public sealed record DesempateDeReglamento(IReadOnlyList<CriterioDeDesempate> Criterios, bool DirectoRecursivo)
{
    public bool Equals(DesempateDeReglamento? otro) =>
        otro is not null && DirectoRecursivo == otro.DirectoRecursivo && Criterios.SequenceEqual(otro.Criterios);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(DirectoRecursivo);
        foreach (var criterio in Criterios) hash.Add(criterio);
        return hash.ToHashCode();
    }
}

/// <summary>Las llaves no tienen orden (R-07 c): cada motivo es un miembro propio, así que el orden de entrada no se conserva.</summary>
public sealed record AdjudicacionDeReglamento(AdjudicacionDeMotivo? WalkOver, AdjudicacionDeMotivo? Sancion, AdjudicacionDeMotivo? Otro)
{
    public AdjudicacionDeMotivo? De(MotivoDeResultado motivo) => motivo switch
    {
        MotivoDeResultado.WalkOver => WalkOver,
        MotivoDeResultado.Sancion => Sancion,
        MotivoDeResultado.Otro => Otro,
        _ => null,
    };
}

/// <summary>El contenido canónico del reglamento, sin el estado (R-01). Cada sección ausente es «no decidida» (R-03, C-13).</summary>
public sealed record ContenidoDeReglamento(
    PuntosDeReglamento? Puntos, DesempateDeReglamento? Desempate, FairPlayDeReglamento? FairPlay, AdjudicacionDeReglamento? Adjudicacion);

/// <param name="Reglamento">El reglamento resuelto, o <c>null</c> si hay errores.</param>
public sealed record ResultadoDeValidacionDeReglamento(
    IReadOnlyList<ErrorDeReglamento> Errores, EstadoDeReglamento? Estado, ContenidoDeReglamento? Contenido);

public static class CodigosDeReglamento
{
    public const string EstadoInvalido = "estado_invalido";
    public const string MiembroRequerido = "miembro_requerido";
    public const string ValorFueraDeRango = "valor_fuera_de_rango";
    public const string PuntosNoDecrecientes = "puntos_no_decrecientes";
    public const string AdjudicacionNoDecreciente = "adjudicacion_no_decreciente";
    public const string CriterioInvalido = "criterio_invalido";
    public const string CriterioRepetido = "criterio_repetido";
    public const string CriteriosDirectosNoContiguos = "criterios_directos_no_contiguos";
    public const string FairPlayRequerido = "fair_play_requerido";
}

/// <summary>
/// Validador puro del reglamento (Spec 009 R-06): sin base de datos, devuelve todos los errores y, si no hay ninguno,
/// el reglamento canónico. Las rutas llevan el prefijo <c>reglamento</c> del cuerpo de la importación.
/// </summary>
public static class ValidadorDeReglamento
{
    public const int MaximoDePuntos = 10;
    public const int MaximoDePesoDeFairPlay = 100;

    public static ResultadoDeValidacionDeReglamento Validar(ReglamentoDescripcion reglamento)
    {
        var errores = new List<ErrorDeReglamento>();
        void Error(string ruta, string codigo) => errores.Add(new ErrorDeReglamento(ruta, codigo));

        // (a)
        EstadoDeReglamento? estado = null;
        if (reglamento.Estado is null) Error("reglamento.estado", CodigosDeReglamento.MiembroRequerido);
        else if (Enum.TryParse(reglamento.Estado, ignoreCase: false, out EstadoDeReglamento e) && Enum.IsDefined(e)) estado = e;
        else Error("reglamento.estado", CodigosDeReglamento.EstadoInvalido);

        var puntos = ValidarPuntos(reglamento.Puntos, Error);
        var desempate = ValidarDesempate(reglamento.Desempate, reglamento.FairPlay is not null, Error);
        var fairPlay = ValidarFairPlay(reglamento.FairPlay, Error);
        var adjudicacion = ValidarAdjudicacion(reglamento.Adjudicacion, Error);

        if (errores.Count > 0 || estado is null) return new ResultadoDeValidacionDeReglamento(errores, null, null);

        return new ResultadoDeValidacionDeReglamento(
            errores, estado, new ContenidoDeReglamento(puntos, desempate, fairPlay, adjudicacion));
    }

    /// <summary>Un entero entre 0 y <paramref name="maximo"/>; <c>null</c> si falta (R-06 i) o está fuera de rango (b, h).</summary>
    private static int? Entero(int? valor, string ruta, int maximo, Action<string, string> error)
    {
        if (valor is null) { error(ruta, CodigosDeReglamento.MiembroRequerido); return null; }
        if (valor is < 0 || valor > maximo) { error(ruta, CodigosDeReglamento.ValorFueraDeRango); return null; }
        return valor;
    }

    private static bool? Booleano(bool? valor, string ruta, Action<string, string> error)
    {
        if (valor is null) error(ruta, CodigosDeReglamento.MiembroRequerido);
        return valor;
    }

    private static PuntosDeReglamento? ValidarPuntos(PuntosDescripcion? puntos, Action<string, string> error)
    {
        if (puntos is null) return null;
        var victoria = Entero(puntos.Victoria, "reglamento.puntos.victoria", MaximoDePuntos, error);
        var empate = Entero(puntos.Empate, "reglamento.puntos.empate", MaximoDePuntos, error);
        var derrota = Entero(puntos.Derrota, "reglamento.puntos.derrota", MaximoDePuntos, error);
        if (victoria is not { } v || empate is not { } e || derrota is not { } d) return null;

        // (c) victoria > empate >= derrota
        if (!(v > e && e >= d))
        {
            error("reglamento.puntos", CodigosDeReglamento.PuntosNoDecrecientes);
            return null;
        }

        return new PuntosDeReglamento(v, e, d);
    }

    private static FairPlayDeReglamento? ValidarFairPlay(FairPlayDescripcion? fairPlay, Action<string, string> error)
    {
        if (fairPlay is null) return null;
        var amarilla = Entero(fairPlay.Amarilla, "reglamento.fairPlay.amarilla", MaximoDePesoDeFairPlay, error);
        var azul = Entero(fairPlay.Azul, "reglamento.fairPlay.azul", MaximoDePesoDeFairPlay, error);
        var roja = Entero(fairPlay.Roja, "reglamento.fairPlay.roja", MaximoDePesoDeFairPlay, error);
        return amarilla is { } a && azul is { } z && roja is { } r ? new FairPlayDeReglamento(a, z, r) : null;
    }

    private static DesempateDeReglamento? ValidarDesempate(DesempateDescripcion? desempate, bool hayFairPlay, Action<string, string> error)
    {
        if (desempate is null) return null;
        var recursivo = Booleano(desempate.DirectoRecursivo, "reglamento.desempate.directoRecursivo", error);

        var valido = true;
        var criterios = new List<CriterioDeDesempate>();
        if (desempate.Criterios is null)
        {
            error("reglamento.desempate.criterios", CodigosDeReglamento.MiembroRequerido);
            return null;
        }

        var vistos = new HashSet<CriterioDeDesempate>();
        for (var i = 0; i < desempate.Criterios.Count; i++)
        {
            var ruta = $"reglamento.desempate.criterios[{i}]";
            // (e) un criterio de la lista cerrada, a lo sumo una vez.
            if (Criterio(desempate.Criterios[i]) is not { } criterio)
            {
                error(ruta, CodigosDeReglamento.CriterioInvalido);
                valido = false;
            }
            else if (!vistos.Add(criterio))
            {
                error(ruta, CodigosDeReglamento.CriterioRepetido);
                valido = false;
            }
            else criterios.Add(criterio);
        }

        // (f) los criterios directos presentes forman un bloque contiguo de la lista.
        var posicionesDirectas = Enumerable.Range(0, desempate.Criterios.Count)
            .Where(i => Criterio(desempate.Criterios[i]) is { } c && EsDirecto(c))
            .ToList();
        if (posicionesDirectas.Count > 0 && posicionesDirectas[^1] - posicionesDirectas[0] + 1 != posicionesDirectas.Count)
        {
            error("reglamento.desempate.criterios", CodigosDeReglamento.CriteriosDirectosNoContiguos);
            valido = false;
        }

        // (g) FairPlay en la lista exige la sección fairPlay.
        if (vistos.Contains(CriterioDeDesempate.FairPlay) && !hayFairPlay)
        {
            error("reglamento.fairPlay", CodigosDeReglamento.FairPlayRequerido);
            valido = false;
        }

        return valido && recursivo is { } r ? new DesempateDeReglamento(criterios, r) : null;
    }

    /// <summary>Solo el nombre exacto de la lista cerrada: ni mayúsculas distintas ni números (R-04).</summary>
    private static CriterioDeDesempate? Criterio(string texto) =>
        Enum.GetNames<CriterioDeDesempate>().Contains(texto, StringComparer.Ordinal) ? Enum.Parse<CriterioDeDesempate>(texto) : null;

    public static bool EsDirecto(CriterioDeDesempate criterio) =>
        criterio is CriterioDeDesempate.PuntosDirectos or CriterioDeDesempate.DiferenciaDeGolesDirecta or CriterioDeDesempate.GolesAFavorDirectos;

    private static AdjudicacionDeReglamento? ValidarAdjudicacion(AdjudicacionDescripcion? adjudicacion, Action<string, string> error)
    {
        if (adjudicacion is null) return null;
        var walkOver = ValidarMotivo(adjudicacion.WalkOver, nameof(AdjudicacionDescripcion.WalkOver), error, out var wo);
        var sancion = ValidarMotivo(adjudicacion.Sancion, nameof(AdjudicacionDescripcion.Sancion), error, out var sa);
        var otro = ValidarMotivo(adjudicacion.Otro, nameof(AdjudicacionDescripcion.Otro), error, out var ot);
        return wo && sa && ot ? new AdjudicacionDeReglamento(walkOver, sancion, otro) : null;
    }

    private static AdjudicacionDeMotivo? ValidarMotivo(
        AdjudicacionDeMotivoDescripcion? motivo, string nombre, Action<string, string> error, out bool correcto)
    {
        correcto = true;
        if (motivo is null) return null;
        var raiz = $"reglamento.adjudicacion.{nombre}";

        var ganador = Entero(motivo.PuntosGanador, $"{raiz}.puntosGanador", MaximoDePuntos, error);
        var perdedor = Entero(motivo.PuntosPerdedor, $"{raiz}.puntosPerdedor", MaximoDePuntos, error);
        var empate = Entero(motivo.PuntosEmpate, $"{raiz}.puntosEmpate", MaximoDePuntos, error);
        var deduccionPerdedor = Entero(motivo.DeduccionPerdedor, $"{raiz}.deduccionPerdedor", MaximoDePuntos, error);
        var deduccionEmpate = Entero(motivo.DeduccionEmpate, $"{raiz}.deduccionEmpate", MaximoDePuntos, error);
        var cuentaComoJugado = Booleano(motivo.CuentaComoJugado, $"{raiz}.cuentaComoJugado", error);
        var enTabla = Booleano(motivo.GolesOficialesEnTabla, $"{raiz}.golesOficialesEnTabla", error);
        var enValla = Booleano(motivo.GolesOficialesEnValla, $"{raiz}.golesOficialesEnValla", error);
        var enGoleadores = Booleano(motivo.GolesDeportivosEnGoleadores, $"{raiz}.golesDeportivosEnGoleadores", error);
        var tarjetas = Booleano(motivo.TarjetasEnFairPlay, $"{raiz}.tarjetasEnFairPlay", error);

        if (ganador is not { } g || perdedor is not { } p || empate is not { } e || deduccionPerdedor is not { } dp
            || deduccionEmpate is not { } de || cuentaComoJugado is not { } cj || enTabla is not { } et
            || enValla is not { } ev || enGoleadores is not { } eg || tarjetas is not { } tf)
        {
            correcto = false;
            return null;
        }

        // (d) puntosGanador >= puntosEmpate >= puntosPerdedor
        if (!(g >= e && e >= p))
        {
            error(raiz, CodigosDeReglamento.AdjudicacionNoDecreciente);
            correcto = false;
            return null;
        }

        return new AdjudicacionDeMotivo(g, p, e, dp, de, cj, et, ev, eg, tf);
    }
}
