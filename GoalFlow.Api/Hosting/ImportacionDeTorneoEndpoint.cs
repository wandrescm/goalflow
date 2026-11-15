using System.Text.Json;
using System.Text.Json.Serialization;
using GoalFlow.Application.Ligas;
using GoalFlow.Application.Torneos;
using GoalFlow.Domain.Torneos;

namespace GoalFlow.Api.Hosting;

/// <summary>
/// <c>PUT /{slug}/admin/torneos/{codigo}</c>: importación idempotente de un torneo completo (Spec 005 R-14 a R-24).
/// Va dentro del grupo de Admin, así que el filtro ya resolvió el slug, el token y la membresía.
/// </summary>
public static class ImportacionDeTorneoEndpoint
{
    /// <summary>1 MiB (R-16). Se lee con un límite explícito: TestServer no aplica MaxRequestBodySize de Kestrel.</summary>
    public const int LimiteDelCuerpo = 1024 * 1024;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // El contrato: cualquier miembro que no esté aquí, incluido cualquier identificador, es un 400 (R-15).
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record PeticionDeJugador(string? Codigo, string? Nombre);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record PeticionDeEquipo(string? Codigo, string? Nombre, bool Retirado, List<PeticionDeJugador?>? Jugadores);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record PeticionDeFase(int Orden, string? Tipo, int Vueltas);

    // La hora programada llega como texto: System.Text.Json aceptaría un DateTimeOffset sin desplazamiento y le
    // pondría el de la máquina (R-17). El dominio la interpreta y exige Z o ±hh:mm.
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record PeticionDePartido(int Fase, int Jornada, string? Local, string? Visitante, string? ProgramadoEn, bool Cancelado);

    // Reglamento (Spec 009 R-05): mismo contrato estricto. Los miembros anulables permiten distinguir Â«ausenteÂ» de Â«ceroÂ»:
    // el validador del dominio rechaza un miembro ausente, nunca le pone un valor por defecto (R-06 i).
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record PeticionDePuntos(int? Victoria, int? Empate, int? Derrota);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record PeticionDeDesempate(List<string?>? Criterios, bool? DirectoRecursivo);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record PeticionDeFairPlay(int? Amarilla, int? Azul, int? Roja);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record PeticionDeAdjudicacionDeMotivo(
        int? PuntosGanador, int? PuntosPerdedor, int? PuntosEmpate, int? DeduccionPerdedor, int? DeduccionEmpate,
        bool? CuentaComoJugado, bool? GolesOficialesEnTabla, bool? GolesOficialesEnValla,
        bool? GolesDeportivosEnGoleadores, bool? TarjetasEnFairPlay);

    // Las llaves son los motivos de adjudicaciÃ³n; cualquier otra es un miembro desconocido y da 400 (R-06).
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record PeticionDeAdjudicacion(
        PeticionDeAdjudicacionDeMotivo? WalkOver, PeticionDeAdjudicacionDeMotivo? Sancion, PeticionDeAdjudicacionDeMotivo? Otro);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record PeticionDeReglamento(
        string? Estado, PeticionDePuntos? Puntos, PeticionDeDesempate? Desempate, PeticionDeFairPlay? FairPlay,
        PeticionDeAdjudicacion? Adjudicacion);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record PeticionDeTorneo(
        string? Nombre, List<PeticionDeFase?>? Fases, List<PeticionDeEquipo?>? Equipos, List<PeticionDePartido?>? Partidos,
        PeticionDeReglamento? Reglamento = null);

    public static RouteGroupBuilder MapImportacionDeTorneo(this RouteGroupBuilder admin)
    {
        admin.MapPut("/torneos/{codigo}", async (
            string codigo, HttpContext http, ImportacionDeTorneo importacion, IIdentidadAutenticada identidad,
            CancellationToken cancellationToken) =>
        {
            if (!FormatoDeCodigos.EsValido(codigo)) return Results.BadRequest();

            var cuerpo = await LectorDeCuerpoLimitado.LeerAsync(
                http.Request.Body, http.Request.ContentLength, LimiteDelCuerpo, cancellationToken);
            if (cuerpo is null) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

            var interpretado = Interpretar(cuerpo);
            if (interpretado is null) return Results.BadRequest();
            var (fixture, reglamento) = interpretado.Value;

            try
            {
                return Results.Ok(await importacion.EjecutarAsync(
                    codigo, fixture, reglamento, identidad.UsuarioId!.Value, cancellationToken));
            }
            catch (FixtureInvalidoException ex)
            {
                return Results.Json(new { errores = ex.Errores }, statusCode: StatusCodes.Status422UnprocessableEntity);
            }
            catch (ConflictoDeImportacionException ex)
            {
                return Results.Json(new { conflictos = ex.Conflictos }, statusCode: StatusCodes.Status409Conflict);
            }
            catch (ConflictoConcurrenteException)
            {
                var conflicto = new ConflictoDeImportacion("concurrencia", codigo);
                return Results.Json(new { conflictos = new[] { conflicto } }, statusCode: StatusCodes.Status409Conflict);
            }
        });

        return admin;
    }

    /// <returns>El fixture y el reglamento opcional, o <c>null</c> si el cuerpo no es JSON válido del contrato (400).</returns>
    private static (FixtureDescripcion Fixture, ReglamentoDescripcion? Reglamento)? Interpretar(byte[] cuerpo)
    {
        PeticionDeTorneo? peticion;
        try
        {
            peticion = JsonSerializer.Deserialize<PeticionDeTorneo>(cuerpo, Json);
        }
        catch (JsonException)
        {
            return null;
        }

        if (peticion is null) return null;
        var fases = peticion.Fases ?? [];
        var equipos = peticion.Equipos ?? [];
        var partidos = peticion.Partidos ?? [];
        if (fases.Any(f => f is null) || equipos.Any(e => e is null) || partidos.Any(p => p is null)
            || equipos.Any(e => e!.Jugadores?.Any(j => j is null) == true))
            return null;

        if (peticion.Reglamento?.Desempate?.Criterios?.Any(c => c is null) == true) return null;

        var fixture = new FixtureDescripcion(
            peticion.Nombre ?? string.Empty,
            [.. fases.Select(f => new FaseDescripcion(f!.Orden, f.Tipo ?? nameof(TipoDeFase.TodosContraTodos), f.Vueltas))],
            [.. equipos.Select(e => new EquipoDescripcion(
                e!.Codigo ?? string.Empty,
                e.Nombre ?? string.Empty,
                e.Retirado,
                [.. (e.Jugadores ?? []).Select(j => new JugadorDescripcion(j!.Codigo ?? string.Empty, j.Nombre ?? string.Empty))]))],
            [.. partidos.Select(p => new PartidoDescripcion(
                p!.Fase, p.Jornada, p.Local ?? string.Empty, p.Visitante ?? string.Empty, p.ProgramadoEn, p.Cancelado))]);

        return (fixture, peticion.Reglamento is { } r ? Reglamento(r) : null);
    }

    private static ReglamentoDescripcion Reglamento(PeticionDeReglamento r) => new(
        r.Estado,
        r.Puntos is { } p ? new PuntosDescripcion(p.Victoria, p.Empate, p.Derrota) : null,
        r.Desempate is { } d ? new DesempateDescripcion(d.Criterios is null ? null : [.. d.Criterios.Select(c => c!)], d.DirectoRecursivo) : null,
        r.FairPlay is { } f ? new FairPlayDescripcion(f.Amarilla, f.Azul, f.Roja) : null,
        r.Adjudicacion is { } a
            ? new AdjudicacionDescripcion(Motivo(a.WalkOver), Motivo(a.Sancion), Motivo(a.Otro))
            : null);

    private static AdjudicacionDeMotivoDescripcion? Motivo(PeticionDeAdjudicacionDeMotivo? m) => m is null
        ? null
        : new AdjudicacionDeMotivoDescripcion(
            m.PuntosGanador, m.PuntosPerdedor, m.PuntosEmpate, m.DeduccionPerdedor, m.DeduccionEmpate,
            m.CuentaComoJugado, m.GolesOficialesEnTabla, m.GolesOficialesEnValla, m.GolesDeportivosEnGoleadores, m.TarjetasEnFairPlay);
}
