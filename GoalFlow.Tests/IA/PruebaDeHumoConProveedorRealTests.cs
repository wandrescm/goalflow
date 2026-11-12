using System.Diagnostics;
using GoalFlow.Application.IA;
using GoalFlow.Domain.Tenancy;
using GoalFlow.Infrastructure.IA;
using GoalFlow.Tests.Isolation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit.Abstractions;

namespace GoalFlow.Tests.IA;

/// <summary>
/// Un <c>[Fact]</c> que se omite si no existe la variable de entorno <c>IA__ClaveApi</c> (Spec 007 §4, CLAUDE.md "Funciones de IA").
/// En CI y en cualquier sesión sin la clave aparece como <b>Omitida</b>, nunca como aprobada. No se agrega ningún paquete.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class HechoConClaveRealAttribute : FactAttribute
{
    public const string Variable = "IA__ClaveApi";

    public HechoConClaveRealAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable)))
            Skip = $"Pendiente: cuenta sin confirmar. Falta la variable de entorno {Variable}; esta prueba llama al proveedor real.";
    }
}

/// <summary>Decora al adaptador real y mide cada llamada: tokens contados, uso facturado, stop_reason, validez y latencia (Spec 007 T-21 b).</summary>
public sealed class GeneradorMedido(IGeneradorDeTexto real, List<string> registro) : IGeneradorDeTexto
{
    private int _ultimoConteo;

    public async Task<int> ContarAsync(SolicitudDeGeneracion solicitud, CancellationToken cancellationToken)
    {
        _ultimoConteo = await real.ContarAsync(solicitud, cancellationToken);
        return _ultimoConteo;
    }

    public async Task<RespuestaGenerada> GenerarAsync(SolicitudDeGeneracion solicitud, CancellationToken cancellationToken)
    {
        var reloj = Stopwatch.StartNew();
        var respuesta = await real.GenerarAsync(solicitud, cancellationToken);
        reloj.Stop();
        var valida = ValidadorDeSalidaDelModelo.Validar(respuesta.Texto).EsValida;
        lock (registro)
            registro.Add(
                $"razonamiento={solicitud.Razonamiento} contados={_ultimoConteo} entrada_facturada={respuesta.TokensEntrada} "
                + $"salida_facturada={respuesta.TokensSalida} stop_reason={respuesta.StopReason} salida_valida={valida} "
                + $"latencia_ms={reloj.ElapsedMilliseconds} texto={respuesta.Texto}");
        return respuesta;
    }
}

/// <summary>
/// La prueba de humo con la clave real (Spec 007 §4): cinco fixtures narrativos por cada modo de razonamiento, cada modo en su propia
/// liga (<c>liga-humo-a</c> para <c>Deshabilitado</c>, <c>liga-humo-b</c> para <c>Adaptativo</c>) y con sus propios cinco partidos:
/// diez claves (tenant, partido, huella) distintas, así que cada corrida es una primera generación (R-16) y ninguna usa ni bloquea el
/// resumen de otra. Cuesta menos de US$0,01. <b>Está implementada y no se ejecuta en la Fase A</b>: el propietario debe confirmar antes
/// la cuenta, el saldo prepago y la recarga automática apagada.
/// </summary>
[Collection(ColeccionDeIa.Nombre)]
public class PruebaDeHumoConProveedorRealTests(BaseDeAislamiento db, ITestOutputHelper salida)
{
    /// <summary>Cinco planillas narrativas, con eventos con nombre; inventadas.</summary>
    private static readonly PlanillaSembrada[] Fixtures =
    [
        new("Jugado", Marcador: (2, 1), Goles: [("Jugador Uno", 1), ("Jugador Dos", 1), ("Jugador Tres", 1)],
            Tarjetas: [("Jugador Cuatro", "Amarilla", 1)]),
        new("Jugado", Marcador: (0, 1), Autogoles: [("Jugador Uno", 1)], Tarjetas: [("Jugador Dos", "Roja", 1)]),
        new("Jugado", Marcador: (3, 3), Goles: [("Jugador Uno", 2), ("Jugador Dos", 1), ("Jugador Tres", 2), ("Jugador Cuatro", 1)],
            TarjetasInformadas: false),
        new("Jugado", Marcador: (2, 0), Goles: [("Jugador Dos", 1)], SinAutor: (1, 0)),
        new("Jugado", Marcador: (0, 0), Tarjetas: [("Jugador Uno", "Amarilla", 2), ("Jugador Tres", "Azul", 1), ("Jugador Cuatro", "Roja", 1)]),
    ];

    private async Task<TenantId> LigaAsync(string slug)
    {
        await db.EjecutarComoSuperusuario($"""
            insert into goalflow.ligas (id, slug, nombre) values ('{Guid.NewGuid()}', '{slug}', 'Liga de humo')
            on conflict (slug) do nothing
            """);
        await using var conexion = new Npgsql.NpgsqlConnection(db.CadenaSuperusuario);
        await conexion.OpenAsync();
        await using var comando = new Npgsql.NpgsqlCommand("select id from goalflow.ligas where slug = @slug", conexion);
        comando.Parameters.AddWithValue("slug", slug);
        return new TenantId((Guid)(await comando.ExecuteScalarAsync())!);
    }

    [HechoConClaveReal]
    public async Task Humo_cinco_fixtures_narrativos_por_cada_modo_de_razonamiento_dejan_diez_llamadas_conciliadas_y_diez_resumenes()
    {
        var clave = Environment.GetEnvironmentVariable(HechoConClaveRealAttribute.Variable)!;
        var partidos = new List<Guid>();
        var registro = new List<string>();

        foreach (var (slug, modo) in new[] { ("liga-humo-a", "Deshabilitado"), ("liga-humo-b", "Adaptativo") })
        {
            var liga = await LigaAsync(slug);
            await using var host = AnfitrionDeIa.Construir(
                db,
                c =>
                {
                    c["IA:ClaveApi"] = clave;
                    c["IA:Razonamiento"] = modo;
                    // La base desechable tiene más de dos ligas: el tope global se ajusta para que se cumpla la cota de R-18.
                    c["IA:TopeMensualPorLigaMicroUsd"] = "1000000";
                    c["IA:TopeMensualGlobalMicroUsd"] = "6000000";
                    c["Mensajeria:TimeoutDelConsumidor"] = "00:02:00";
                    c["IA:TimeoutDeLlamada"] = "00:01:00";
                },
                servicios: s =>
                {
                    s.RemoveAll<IGeneradorDeTexto>();
                    s.AddScoped<IGeneradorDeTexto>(sp => new GeneradorMedido(
                        new AdaptadorAnthropic(
                            new HttpClient { BaseAddress = new Uri(AdaptadorAnthropic.UrlBase), Timeout = Timeout.InfiniteTimeSpan },
                            sp.GetRequiredService<OpcionesDeIA>()),
                        registro));
                },
                conGenerador: false);

            foreach (var fixture in Fixtures)
            {
                var partido = await PartidoSembrado.CrearAsync(db, liga);
                await partido.GuardarAsync(fixture);
                partidos.Add(partido.Id);
                await host.ProcesarAsync(partido, 1);
            }
        }

        foreach (var linea in registro) salida.WriteLine(linea);

        var ids = string.Join(",", partidos.Select(p => $"'{p}'"));
        await using var anfitrionDeMedicion = AnfitrionDeIa.Construir(db, conGenerador: false);
        Assert.Equal(10, await anfitrionDeMedicion.Contar(
            $"select count(*) from goalflow.llamadas_ia where partido_id in ({ids}) and estado = 'Conciliada'"));
        Assert.Equal(10, await anfitrionDeMedicion.Contar(
            $"select count(*) from goalflow.resumenes_partido where partido_id in ({ids}) and origen = 'Modelo' and estado = 'PorVerificar'"));
        var excesos = await anfitrionDeMedicion.Contar(
            $"select count(*) from goalflow.llamadas_ia where partido_id in ({ids}) and resultado like '%_exceso'");
        salida.WriteLine($"excesos de costo sobre la reserva: {excesos} de 10");
        Assert.Equal(10, registro.Count);
    }
}
