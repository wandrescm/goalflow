using System.Text.Json;
using System.Text.Json.Serialization;
using GoalFlow.Application.Ligas;
using GoalFlow.Application.Torneos;
using GoalFlow.Domain.Torneos;

namespace GoalFlow.Api.Hosting;

/// <summary>
/// <c>GET</c> y <c>PUT /{slug}/admin/torneos/{codigo}/fases/{orden}/partidos/{local}/{visitante}/planilla</c> (Spec 006 R-08 a
/// R-22). Van dentro del grupo de Admin: el filtro ya resolvió el slug, el token y la membresía.
/// </summary>
public static class PlanillaEndpoints
{
    /// <summary>64 KiB (R-10). Se lee con el lector acotado de Spec 005 C-23.</summary>
    public const int LimiteDelCuerpo = 64 * 1024;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // El contrato: cualquier miembro que no esté aquí, incluidos <c>version</c> y cualquier identificador, es un 400 (R-10).
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record PeticionDeMarcador(int? Local, int? Visitante);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record PeticionDeGol(string? Jugador, int Cantidad);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record PeticionDeTarjeta(string? Jugador, string? Color, int Cantidad);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class PeticionDePlanilla
    {
        public required int VersionEsperada { get; init; }

        public required string Estado { get; init; }

        public PeticionDeMarcador? Marcador { get; init; }

        public PeticionDeMarcador? Oficial { get; init; }

        public string? Motivo { get; init; }

        public string? MotivoRectificacion { get; init; }

        public bool? TarjetasInformadas { get; init; }

        public List<PeticionDeGol?>? Goles { get; init; }

        public List<PeticionDeGol?>? Autogoles { get; init; }

        public PeticionDeMarcador? GolesSinAutor { get; init; }

        public List<PeticionDeTarjeta?>? Tarjetas { get; init; }
    }

    /// <summary>Los mismos miembros que el <c>PUT</c>, más <c>version</c> y sin <c>versionEsperada</c> (R-16).</summary>
    public sealed record RespuestaDePlanilla(
        int Version,
        string Estado,
        PeticionDeMarcador? Marcador,
        PeticionDeMarcador? Oficial,
        string? Motivo,
        string? MotivoRectificacion,
        bool? TarjetasInformadas,
        IReadOnlyList<PeticionDeGol> Goles,
        IReadOnlyList<PeticionDeGol> Autogoles,
        PeticionDeMarcador GolesSinAutor,
        IReadOnlyList<PeticionDeTarjeta> Tarjetas);

    public static RouteGroupBuilder MapPlanilla(this RouteGroupBuilder admin)
    {
        const string ruta = "/torneos/{codigo}/fases/{orden}/partidos/{local}/{visitante}/planilla";

        admin.MapGet(ruta, async (
            string codigo, string orden, string local, string visitante, ConsultaDePlanilla consulta, CancellationToken cancellationToken) =>
        {
            if (ClaveDe(codigo, orden, local, visitante) is not { } clave) return Results.BadRequest();

            try
            {
                var vista = await consulta.EjecutarAsync(clave, cancellationToken);
                return Results.Ok(Responder(vista));
            }
            catch (PartidoInexistenteException)
            {
                return Results.NotFound();
            }
        });

        admin.MapPut(ruta, async (
            string codigo, string orden, string local, string visitante, HttpContext http, IIdentidadAutenticada identidad,
            GuardarPlanilla guardar, CancellationToken cancellationToken) =>
        {
            if (ClaveDe(codigo, orden, local, visitante) is not { } clave) return Results.BadRequest();

            var cuerpo = await LectorDeCuerpoLimitado.LeerAsync(
                http.Request.Body, http.Request.ContentLength, LimiteDelCuerpo, cancellationToken);
            if (cuerpo is null) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

            if (Interpretar(cuerpo) is not { } peticion) return Results.BadRequest();

            try
            {
                var respuesta = await guardar.EjecutarAsync(
                    clave, identidad.UsuarioId!.Value, peticion.VersionEsperada, peticion.Planilla, cancellationToken);
                return Results.Ok(new { resultado = respuesta.Resultado, version = respuesta.Version });
            }
            catch (PartidoInexistenteException)
            {
                return Results.NotFound();
            }
            catch (PlanillaInvalidaException ex)
            {
                return Results.Json(new { errores = ex.Errores }, statusCode: StatusCodes.Status422UnprocessableEntity);
            }
            catch (PartidoCanceladoException)
            {
                return Results.Json(new { codigo = "partido_cancelado" }, statusCode: StatusCodes.Status409Conflict);
            }
            catch (ConflictoDeVersionException ex)
            {
                return Results.Json(new { codigo = "conflicto_de_version", version = ex.VersionActual }, statusCode: StatusCodes.Status409Conflict);
            }
        });

        return admin;
    }

    /// <returns>La clave del partido, o <c>null</c> si algún valor de la ruta está mal formado (400, R-08).</returns>
    private static ClaveDePartido? ClaveDe(string codigo, string orden, string local, string visitante) =>
        FormatoDeCodigos.EsValido(codigo) && FormatoDeCodigos.EsValido(local) && FormatoDeCodigos.EsValido(visitante)
        && int.TryParse(orden, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var fase)
        && fase >= 1
            ? new ClaveDePartido(codigo, fase, local, visitante)
            : null;

    /// <returns>La versión esperada y la planilla, o <c>null</c> si el cuerpo no es JSON válido del contrato (400).</returns>
    private static (int VersionEsperada, PlanillaDescripcion Planilla)? Interpretar(byte[] cuerpo)
    {
        PeticionDePlanilla? peticion;
        try
        {
            peticion = JsonSerializer.Deserialize<PeticionDePlanilla>(cuerpo, Json);
        }
        catch (JsonException)
        {
            return null;
        }

        if (peticion is null || peticion.VersionEsperada < 0) return null;
        var goles = peticion.Goles ?? [];
        var autogoles = peticion.Autogoles ?? [];
        var tarjetas = peticion.Tarjetas ?? [];
        if (goles.Any(g => g is null) || autogoles.Any(g => g is null) || tarjetas.Any(t => t is null)) return null;

        MarcadorDescripcion? Par(PeticionDeMarcador? m) => m is null ? null : new MarcadorDescripcion(m.Local, m.Visitante);

        return (peticion.VersionEsperada, new PlanillaDescripcion(
            peticion.Estado,
            Par(peticion.Marcador),
            Par(peticion.Oficial),
            peticion.Motivo,
            peticion.MotivoRectificacion,
            peticion.TarjetasInformadas,
            [.. goles.Select(g => new GolDeJugadorDescripcion(g!.Jugador ?? string.Empty, g.Cantidad))],
            [.. autogoles.Select(g => new GolDeJugadorDescripcion(g!.Jugador ?? string.Empty, g.Cantidad))],
            Par(peticion.GolesSinAutor) ?? new MarcadorDescripcion(0, 0),
            [.. tarjetas.Select(t => new TarjetaDescripcion(t!.Jugador ?? string.Empty, t.Color ?? string.Empty, t.Cantidad))]));
    }

    private static RespuestaDePlanilla Responder(VistaDePlanilla vista)
    {
        var p = vista.Planilla;
        PeticionDeMarcador? Par(MarcadorDescripcion? m) => m is null ? null : new PeticionDeMarcador(m.Local, m.Visitante);

        return new RespuestaDePlanilla(
            vista.Version,
            p.Estado,
            Par(p.Marcador),
            Par(p.Oficial),
            p.Motivo,
            p.MotivoRectificacion,
            p.TarjetasInformadas,
            [.. p.Goles.Select(g => new PeticionDeGol(g.Jugador, g.Cantidad))],
            [.. p.Autogoles.Select(g => new PeticionDeGol(g.Jugador, g.Cantidad))],
            Par(p.GolesSinAutor)!,
            [.. p.Tarjetas.Select(t => new PeticionDeTarjeta(t.Jugador, t.Color, t.Cantidad))]);
    }
}
