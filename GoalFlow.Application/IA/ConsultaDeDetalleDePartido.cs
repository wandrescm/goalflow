using GoalFlow.Application.Persistencia;
using GoalFlow.Application.Tenancy;
using GoalFlow.Application.Torneos.Contracts;
using Microsoft.Extensions.Logging;

namespace GoalFlow.Application.IA;

// ---- El cuerpo público del detalle de un partido (Spec 011 R-09). Sin identificadores, versión ni datos administrativos (R-13). ----
// El orden de los miembros de cada record es el del cuerpo JSON.

public sealed record TorneoDelDetalle(string Codigo, string Nombre);

public sealed record FaseDelDetalle(int Orden);

public sealed record EquipoDelDetalle(string Codigo, string Nombre);

public sealed record MarcadorDelDetalle(int Local, int Visitante);

public sealed record JugadorDelDetalle(string Codigo, string Nombre);

/// <summary><c>Equipo</c> es <c>local</c> o <c>visitante</c>: el equipo al que se acreditan los goles.</summary>
public sealed record GolDelDetalle(string Equipo, JugadorDelDetalle Jugador, int Cantidad);

/// <summary>Un autogol del jugador, que juega en <c>EquipoDelAutor</c>; se acredita a <c>EquipoBeneficiado</c> (el otro lado).</summary>
public sealed record AutogolDelDetalle(string EquipoDelAutor, string EquipoBeneficiado, JugadorDelDetalle Jugador, int Cantidad);

/// <summary><c>Color</c> es <c>Amarilla</c>, <c>Azul</c> o <c>Roja</c>.</summary>
public sealed record TarjetaDelDetalle(string Equipo, JugadorDelDetalle Jugador, string Color, int Cantidad);

/// <summary>El resumen publicado (Spec 011 R-14, C-09): su texto y su origen, <c>Modelo</c> o <c>Plantilla</c>. Nada más de la fila.</summary>
public sealed record ResumenDelDetalle(string Texto, string Origen);

public sealed record DetalleDePartido(
    TorneoDelDetalle Torneo, FaseDelDetalle Fase, int Jornada, DateTime? ProgramadoPara,
    EquipoDelDetalle Local, EquipoDelDetalle Visitante, string Estado,
    MarcadorDelDetalle? Marcador, MarcadorDelDetalle? Oficial, string? Motivo,
    IReadOnlyList<GolDelDetalle> Goles, IReadOnlyList<AutogolDelDetalle> Autogoles, MarcadorDelDetalle? GolesSinAutor,
    bool? TarjetasInformadas, IReadOnlyList<TarjetaDelDetalle> Tarjetas, ResumenDelDetalle? Resumen);

/// <summary>El resultado del caso de uso: distingue <i>encontrado</i> de <i>liga, torneo, fase o partido inexistente</i> (Spec 011 R-08).</summary>
public sealed record ResultadoDeDetalleDePartido(bool Encontrado, DetalleDePartido? Detalle)
{
    public static ResultadoDeDetalleDePartido Ok(DetalleDePartido detalle) => new(true, detalle);

    public static ResultadoDeDetalleDePartido NoEncontrado() => new(false, null);
}

/// <summary>
/// El detalle público de un partido por su clave natural (Spec 011 R-08 a R-19). Una sola transacción <c>REPEATABLE READ</c>: los hechos
/// de la planilla vigente salen de un solo objeto del puerto de Torneos (R-10, R-20). No escribe nada, no llama al proveedor y no
/// publica eventos (R-18, R-19).
/// </summary>
public sealed class ConsultaDeDetalleDePartido(
    IUnidadDeTrabajo unidadDeTrabajo, ILectorDePartidoParaResumen lector, ITenantContext tenantContext,
    ILogger<ConsultaDeDetalleDePartido> logger)
{
    public async Task<ResultadoDeDetalleDePartido> EjecutarAsync(ClaveNaturalDePartido clave, CancellationToken cancellationToken)
    {
        DetalleDePartido? detalle = null;
        await using (var transaccion = await unidadDeTrabajo.IniciarLecturaConsistenteAsync(cancellationToken))
        {
            var porClave = await lector.LeerPorClaveAsync(clave, cancellationToken);
            if (porClave is not null) detalle = ArmarHechos(clave, porClave.Datos);
            await transaccion.ConfirmarAsync(cancellationToken);
        }

        // Nunca nombres, textos ni identificadores (R-26).
        logger.LogInformation(
            "Lectura pública: liga {Liga}, ruta {Ruta}, torneo {Torneo}, fase {Fase}, goles {Goles}, tarjetas {Tarjetas}, encontrado {Encontrado}",
            tenantContext.Current.Value, "detalle", clave.CodigoTorneo, clave.OrdenFase,
            detalle?.Goles.Count ?? 0, detalle?.Tarjetas.Count ?? 0, detalle is not null);
        return detalle is null ? ResultadoDeDetalleDePartido.NoEncontrado() : ResultadoDeDetalleDePartido.Ok(detalle);
    }

    private static string Lado(LadoDelEquipo lado) => lado == LadoDelEquipo.Local ? "local" : "visitante";

    private static MarcadorDelDetalle? Marcador(MarcadorDelPartido? m) => m is null ? null : new MarcadorDelDetalle(m.Local, m.Visitante);

    /// <summary>
    /// Los hechos del cuerpo (R-09 a R-12), todos del mismo objeto que lee el puerto (R-10). Con el código de torneo, de fase y de
    /// equipos de la ruta, que el puerto comparó exactamente. Sin marcador en la planilla vigente se aplica R-11.
    /// </summary>
    private static DetalleDePartido ArmarHechos(ClaveNaturalDePartido clave, DatosDePartidoParaResumen d)
    {
        var conMarcador = d.Marcador is not null;
        var informadas = conMarcador ? d.TarjetasInformadas : null;

        // Orden R-12: lado (local primero), nombre, código y, en tarjetas, color; todo ordinal.
        var goles = !conMarcador ? [] : d.Goles
            .OrderBy(g => g.Equipo).ThenBy(g => g.Jugador, StringComparer.Ordinal).ThenBy(g => g.CodigoDeInscripcion, StringComparer.Ordinal)
            .Select(g => new GolDelDetalle(Lado(g.Equipo), new JugadorDelDetalle(g.CodigoDeInscripcion, g.Jugador), g.Cantidad))
            .ToList();
        var autogoles = !conMarcador ? [] : d.Autogoles
            .OrderBy(a => a.EquipoDelAutor).ThenBy(a => a.Jugador, StringComparer.Ordinal).ThenBy(a => a.CodigoDeInscripcion, StringComparer.Ordinal)
            .Select(a => new AutogolDelDetalle(
                Lado(a.EquipoDelAutor), Lado(a.EquipoDelAutor == LadoDelEquipo.Local ? LadoDelEquipo.Visitante : LadoDelEquipo.Local),
                new JugadorDelDetalle(a.CodigoDeInscripcion, a.Jugador), a.Cantidad))
            .ToList();
        var tarjetas = !conMarcador || informadas != true ? [] : d.Tarjetas
            .OrderBy(t => t.Equipo).ThenBy(t => t.Jugador, StringComparer.Ordinal).ThenBy(t => t.CodigoDeInscripcion, StringComparer.Ordinal)
            .ThenBy(t => t.Color, StringComparer.Ordinal)
            .Select(t => new TarjetaDelDetalle(Lado(t.Equipo), new JugadorDelDetalle(t.CodigoDeInscripcion, t.Jugador), t.Color, t.Cantidad))
            .ToList();

        return new DetalleDePartido(
            new TorneoDelDetalle(clave.CodigoTorneo, d.Torneo), new FaseDelDetalle(clave.OrdenFase), d.Jornada, d.ProgramadoPara,
            new EquipoDelDetalle(clave.CodigoLocal, d.Local), new EquipoDelDetalle(clave.CodigoVisitante, d.Visitante), d.Estado,
            Marcador(d.Marcador), Marcador(d.Oficial), d.Motivo,
            goles, autogoles, conMarcador ? Marcador(d.GolesSinAutor) : null, informadas, tarjetas, Resumen: null);
    }
}
