using System.Diagnostics;
using System.Text.Json;
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

/// <summary>El escenario en curso; lo fija la prueba antes de procesar cada partido.</summary>
public sealed class ContextoDeMedicion
{
    public int Escenario { get; set; }

    public string Modo { get; set; } = "";
}

/// <summary>Una llamada pagada medida. Nunca lleva la clave, encabezados ni mensajes de excepción del proveedor.</summary>
public sealed record MedicionDeLlamada(
    int Escenario, string Modo, int TokensContados, int? TokensEntradaFacturados, int? TokensSalidaFacturados, string? StopReason,
    bool? SalidaValida, long LatenciaMs, long? CostoMicroUsd, string? ErrorTipo, int? ErrorEstado, string? ErrorTipoProveedor, string? Texto);

/// <summary>Decora al adaptador real y mide cada llamada: tokens contados, uso facturado, stop_reason, validez, latencia y costo (Spec 007 T-21 b).</summary>
public sealed class GeneradorMedido(
    IGeneradorDeTexto real, List<MedicionDeLlamada> registro, ContextoDeMedicion contexto, PreciosDeIa precios) : IGeneradorDeTexto
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
        try
        {
            var respuesta = await real.GenerarAsync(solicitud, cancellationToken);
            reloj.Stop();
            var valida = ValidadorDeSalidaDelModelo.Validar(respuesta.Texto).EsValida;
            Anotar(new MedicionDeLlamada(
                contexto.Escenario, contexto.Modo, _ultimoConteo, respuesta.TokensEntrada, respuesta.TokensSalida, respuesta.StopReason,
                valida, reloj.ElapsedMilliseconds, CalculoDeCosto.CostoReal(respuesta.TokensEntrada, respuesta.TokensSalida, precios),
                null, null, null, respuesta.Texto));
            return respuesta;
        }
        catch (Exception ex) when (ex is RespuestaConErrorException or ResultadoInciertoException or SalidaInvalidaException)
        {
            // Solo el tipo y, si lo hay, el estado y el error.type documentado; nunca el mensaje.
            reloj.Stop();
            var uso = (ex as RespuestaConErrorException)?.Uso ?? (ex as SalidaInvalidaException)?.Uso;
            Anotar(new MedicionDeLlamada(
                contexto.Escenario, contexto.Modo, _ultimoConteo, uso?.TokensEntrada, uso?.TokensSalida, null, null,
                reloj.ElapsedMilliseconds, uso is null ? null : CalculoDeCosto.CostoReal(uso.TokensEntrada, uso.TokensSalida, precios),
                ex.GetType().Name, (ex as RespuestaConErrorException)?.Estado, (ex as RespuestaConErrorException)?.TipoDeError, null));
            throw;
        }
    }

    private void Anotar(MedicionDeLlamada medicion)
    {
        lock (registro) registro.Add(medicion);
    }
}

/// <summary>
/// La confirmación de T-21 b con la clave real, en la configuración de producción (Spec 007 v5 §4): cinco fixtures narrativos en una sola
/// liga (<c>liga-humo-a</c>), con el <c>IA:Razonamiento</c> efectivo del registro, que la prueba no fija sino que comprueba antes de la
/// primera llamada pagada. Cinco claves (tenant, partido, huella) distintas: cada una es una primera generación (R-16). Los resultados y
/// los textos se escriben en <c>GOALFLOW_HUMO_SALIDA</c> (fuera del repo), aun si una aserción falla; un fallo reintentable de un escenario
/// se anota y no detiene a los demás.
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

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

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

    /// <summary>La carpeta de evidencia: absoluta y fuera de cualquier repositorio git. Se valida antes de la primera llamada pagada.</summary>
    private static string CarpetaDeSalida()
    {
        var ruta = Environment.GetEnvironmentVariable("GOALFLOW_HUMO_SALIDA");
        if (string.IsNullOrWhiteSpace(ruta) || !Path.IsPathFullyQualified(ruta))
            throw new InvalidOperationException("GOALFLOW_HUMO_SALIDA debe ser una ruta absoluta; se aborta antes de llamar al proveedor.");
        for (var d = new DirectoryInfo(Path.GetFullPath(ruta)); d is not null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, ".git")) || File.Exists(Path.Combine(d.FullName, ".git")))
                throw new InvalidOperationException("GOALFLOW_HUMO_SALIDA no puede estar dentro de un repositorio git.");
        Directory.CreateDirectory(ruta);
        return ruta;
    }

    /// <summary>
    /// Cinco fixtures narrativos en la configuración de producción dejan cinco llamadas conciliadas y cinco resúmenes <c>PorVerificar</c>,
    /// sin excesos ni fallos y con cinco salidas válidas (Spec 007 v5 §4, T-21 b). Antes de pagar, comprueba que el modo efectivo es
    /// <c>Deshabilitado</c> sin imponerlo.
    /// </summary>
    [HechoConClaveReal]
    public async Task Humo_cinco_fixtures_narrativos_en_la_configuracion_de_produccion_dejan_cinco_llamadas_conciliadas_y_cinco_resumenes()
    {
        var carpeta = CarpetaDeSalida();
        var clave = Environment.GetEnvironmentVariable(HechoConClaveRealAttribute.Variable)!;
        var partidos = new List<Guid>();
        var registro = new List<MedicionDeLlamada>();
        var contexto = new ContextoDeMedicion();
        var fallos = new List<object>();

        try
        {
            {
                var liga = await LigaAsync("liga-humo-a");
                await using var host = AnfitrionDeIa.Construir(
                    db,
                    c =>
                    {
                        c["IA:ClaveApi"] = clave;
                        // La base desechable tiene tres ligas: el tope global se ajusta para que se cumpla la cota de R-18.
                        c["IA:TopeMensualPorLigaMicroUsd"] = "1000000";
                        c["IA:TopeMensualGlobalMicroUsd"] = "6000000";
                        c["Mensajeria:TimeoutDelConsumidor"] = "00:02:00";
                        // El validador de mensajería exige TimeoutDelConsumidor < DuracionDelLease; el valor de prueba (10 s) no lo cumple.
                        c["Mensajeria:DuracionDelLease"] = "00:03:00";
                        c["IA:TimeoutDeLlamada"] = "00:01:00";
                    },
                    servicios: s =>
                    {
                        s.RemoveAll<IGeneradorDeTexto>();
                        s.AddScoped<IGeneradorDeTexto>(sp =>
                        {
                            var opciones = sp.GetRequiredService<OpcionesDeIA>();
                            return new GeneradorMedido(
                                new AdaptadorAnthropic(
                                    new HttpClient { BaseAddress = new Uri(AdaptadorAnthropic.UrlBase), Timeout = Timeout.InfiniteTimeSpan },
                                    opciones),
                                registro, contexto, opciones.Precios);
                        });
                    },
                    conGenerador: false);

                // Antes de pagar: el modo efectivo, tal como lo resuelve el registro. Si no es Deshabilitado, no hay ninguna llamada.
                var efectivas = host.Services.GetRequiredService<OpcionesDeIA>();
                Assert.Equal(RazonamientoDeIA.Deshabilitado, efectivas.Razonamiento);
                var modo = efectivas.Razonamiento.ToString();

                foreach (var fixture in Fixtures)
                {
                    contexto.Escenario = partidos.Count + 1;
                    contexto.Modo = modo;
                    var partido = await PartidoSembrado.CrearAsync(db, liga);
                    await partido.GuardarAsync(fixture);
                    partidos.Add(partido.Id);

                    // Un fallo reintentable (p. ej. un corte por max_tokens) es un resultado de la medición: se anota y siguen los demás
                    // escenarios. Solo el tipo de la excepción; nunca su mensaje.
                    try
                    {
                        await host.ProcesarAsync(partido, 1);
                    }
                    catch (FalloReintentableException ex)
                    {
                        fallos.Add(new { escenario = contexto.Escenario, modo, codigo = ex.CodigoDeRazon });
                    }
                }
            }
        }
        finally
        {
            await EscribirEvidenciaAsync(carpeta, partidos, registro, fallos);
        }

        var ids = string.Join(",", partidos.Select(p => $"'{p}'"));
        await using var anfitrionDeMedicion = AnfitrionDeIa.Construir(db, conGenerador: false);
        Assert.Equal(5, await anfitrionDeMedicion.Contar(
            $"select count(*) from goalflow.llamadas_ia where partido_id in ({ids}) and estado = 'Conciliada'"));
        Assert.Equal(5, await anfitrionDeMedicion.Contar(
            $"select count(*) from goalflow.resumenes_partido where partido_id in ({ids}) and origen = 'Modelo' and estado = 'PorVerificar'"));
        var excesos = await anfitrionDeMedicion.Contar(
            $"select count(*) from goalflow.llamadas_ia where partido_id in ({ids}) and resultado like '%_exceso'");
        salida.WriteLine($"excesos de costo sobre la reserva: {excesos} de 5");
        Assert.Equal(0, excesos);
        Assert.Empty(fallos);
        Assert.Equal(5, registro.Count);
        Assert.All(registro, m =>
        {
            Assert.Equal("end_turn", m.StopReason);
            Assert.True(m.SalidaValida);
        });
    }

    /// <summary>Escribe resultados y textos en la carpeta, aun si una aserción falla después. Nunca incluye la clave ni la configuración.</summary>
    private async Task EscribirEvidenciaAsync(string carpeta, List<Guid> partidos, List<MedicionDeLlamada> registro, List<object> fallos)
    {
        List<object> filasDeBase = [];
        try
        {
            if (partidos.Count > 0)
            {
                await using var anfitrion = AnfitrionDeIa.Construir(db, conGenerador: false);
                var ids = string.Join(",", partidos.Select(p => $"'{p}'"));
                foreach (var f in await anfitrion.Filas(
                    "select l.partido_id, l.numero, l.estado, l.reserva, l.tokens_entrada, l.tokens_salida, l.costo, l.resultado "
                    + $"from goalflow.llamadas_ia l where l.partido_id in ({ids}) order by l.creado_en"))
                {
                    filasDeBase.Add(new
                    {
                        partido = f[0],
                        numero = f[1],
                        estado = f[2],
                        reservaMicroUsd = f[3],
                        tokensEntrada = f[4],
                        tokensSalida = f[5],
                        costoMicroUsd = f[6],
                        resultado = f[7],
                        excesoMicroUsd = f[6] is long costo && f[3] is long reserva && costo > reserva ? costo - reserva : 0L,
                    });
                }
            }
        }
        catch (Exception ex)
        {
            filasDeBase.Add(new { errorAlLeerLaBase = ex.GetType().Name });
        }

        // llamadas.json: las métricas de las llamadas efectivamente realizadas y el resultado de cada escenario (sin los textos).
        MedicionDeLlamada[] medidas;
        lock (registro) medidas = [.. registro];
        var llamadas = medidas.Select(m => m with { Texto = null }).ToList();
        await File.WriteAllTextAsync(
            Path.Combine(carpeta, "llamadas.json"),
            JsonSerializer.Serialize(
                new { escenariosIniciados = partidos.Count, llamadas, fallosDelConsumidor = fallos, llamadasEnBase = filasDeBase }, Json));

        // textos.md: solo los textos que el proveedor devolvió, por escenario; nada se inventa.
        var md = new System.Text.StringBuilder("# Textos de la prueba de humo T-21(b)\n");
        foreach (var m in medidas.OrderBy(m => m.Escenario))
        {
            md.Append($"\n## Escenario {m.Escenario} · {m.Modo}\n\n")
              .Append($"stop_reason={m.StopReason} · salida_valida={m.SalidaValida}\n\n");
            md.Append(string.IsNullOrEmpty(m.Texto) ? "_Sin texto devuelto por el proveedor._\n" : $"```\n{m.Texto}\n```\n");
        }

        for (var i = 1; i <= partidos.Count; i++)
            if (!medidas.Any(m => m.Escenario == i))
                md.Append($"\n## Escenario {i}\n\n_Sin llamada pagada ni texto._\n");

        await File.WriteAllTextAsync(Path.Combine(carpeta, "textos.md"), md.ToString());
        salida.WriteLine($"evidencia escrita: {medidas.Length} llamadas medidas, {fallos.Count} fallos del consumidor");
    }
}
