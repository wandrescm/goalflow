using System.Text.Json;
using System.Text.Json.Serialization;
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

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record PeticionDeTorneo(
        string? Nombre, List<PeticionDeFase?>? Fases, List<PeticionDeEquipo?>? Equipos, List<PeticionDePartido?>? Partidos);

    public static RouteGroupBuilder MapImportacionDeTorneo(this RouteGroupBuilder admin)
    {
        admin.MapPut("/torneos/{codigo}", async (
            string codigo, HttpContext http, ImportacionDeTorneo importacion, CancellationToken cancellationToken) =>
        {
            if (!FormatoDeCodigos.EsValido(codigo)) return Results.BadRequest();

            var cuerpo = await LeerCuerpoAsync(http.Request, cancellationToken);
            if (cuerpo is null) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

            var fixture = Interpretar(cuerpo);
            if (fixture is null) return Results.BadRequest();

            try
            {
                return Results.Ok(await importacion.EjecutarAsync(codigo, fixture, cancellationToken));
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

    /// <returns>Los bytes del cuerpo, o <c>null</c> si superan el límite.</returns>
    private static async Task<byte[]?> LeerCuerpoAsync(HttpRequest peticion, CancellationToken cancellationToken)
    {
        if (peticion.ContentLength > LimiteDelCuerpo) return null;

        using var memoria = new MemoryStream();
        var buffer = new byte[16 * 1024];
        int leidos;
        while ((leidos = await peticion.Body.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (memoria.Length + leidos > LimiteDelCuerpo) return null;
            memoria.Write(buffer, 0, leidos);
        }

        return memoria.ToArray();
    }

    /// <returns>El fixture, o <c>null</c> si el cuerpo no es JSON válido del contrato (400).</returns>
    private static FixtureDescripcion? Interpretar(byte[] cuerpo)
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

        return new FixtureDescripcion(
            peticion.Nombre ?? string.Empty,
            [.. fases.Select(f => new FaseDescripcion(f!.Orden, f.Tipo ?? nameof(TipoDeFase.TodosContraTodos), f.Vueltas))],
            [.. equipos.Select(e => new EquipoDescripcion(
                e!.Codigo ?? string.Empty,
                e.Nombre ?? string.Empty,
                e.Retirado,
                [.. (e.Jugadores ?? []).Select(j => new JugadorDescripcion(j!.Codigo ?? string.Empty, j.Nombre ?? string.Empty))]))],
            [.. partidos.Select(p => new PartidoDescripcion(
                p!.Fase, p.Jornada, p.Local ?? string.Empty, p.Visitante ?? string.Empty, p.ProgramadoEn, p.Cancelado))]);
    }
}
