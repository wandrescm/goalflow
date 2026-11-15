using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using GoalFlow.Application.Torneos.Contracts;
using GoalFlow.Infrastructure.Persistencia;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static GoalFlow.Tests.Isolation.EntornoDeEstadisticas;
using static GoalFlow.Tests.Mensajeria.AyudantesDeMensajeria;

namespace GoalFlow.Tests.Isolation;

/// <summary>Marca, en el anfitrión de pruebas, la petición que lleva el encabezado de la compuerta: solo ella se retiene (Spec 009 T-16).</summary>
public sealed class FiltroDeCompuerta(CompuertaDeLecturaConsistente compuerta) : IStartupFilter
{
    public const string Encabezado = "X-Prueba-Compuerta";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> siguiente) => app =>
    {
        app.Use(async (contexto, next) =>
        {
            if (contexto.Request.Headers.ContainsKey(Encabezado)) compuerta.Activar();
            await next();
        });
        siguiente(app);
    };
}

/// <summary>
/// Spec 009 T-16 y T-20, con compuertas (Spec 006 C-24): cada prueba afirma el orden de eventos observado; una barrera alrededor de
/// dos peticiones no es prueba. Las compuertas solo existen en el anfitrión de pruebas.
/// </summary>
[Collection(ColeccionDePlanilla.Nombre)]
public partial class ConcurrenciaDeEstadisticasTests(BaseDeAislamiento db)
{
    [GeneratedRegex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex PatronDeGuid();

    private static void SinFugas(string texto)
    {
        Assert.DoesNotMatch(PatronDeGuid(), texto);
        foreach (var prohibido in new[] { "fk_", "ux_", "ak_", "23503", "23505", "constraint", "xmin", "Npgsql" })
            Assert.DoesNotContain(prohibido, texto, StringComparison.OrdinalIgnoreCase);
    }

    // ---- T-16: la lectura de las posiciones es una foto única ----

    /// <summary>La corrección de M1 (E1–E2): de 2–1 a 1–2, con otras filas de goles.</summary>
    private static PlanillaJ CorreccionDeM1(string p) => new(1, "Jugado", new(1, 2), TarjetasInformadas: true,
        Goles: [new(Jugador(p, "11"), 1), new(Jugador(p, "21"), 2)], Tarjetas: [new(Jugador(p, "11"), "Amarilla", 1)]);

    [Fact]
    public async Task T16_la_respuesta_retenida_es_igual_a_la_de_antes_de_la_correccion_y_una_posterior_a_la_de_despues()
    {
        var compuerta = new CompuertaDeLecturaConsistente();
        await using var e = new EntornoDeEstadisticas(db, ajustar: b => b.ConfigureTestServices(s =>
        {
            s.ConfigureDbContext<GoalFlowDbContext>(o => o.AddInterceptors(compuerta));
            s.AddSingleton<IStartupFilter>(new FiltroDeCompuerta(compuerta));
        }));
        var p = Prefijo();
        await e.CargarF6("liga-a", p, ReglasDeF6());
        var antes = (await Leer(await e.Get(Posiciones("liga-a", p)))).Texto;

        // La petición retenida: se detiene después de su primer comando del puerto, ya dentro de la transacción de lectura.
        var peticion = new HttpRequestMessage(HttpMethod.Get, Posiciones("liga-a", p));
        peticion.Headers.Add(FiltroDeCompuerta.Encabezado, "1");
        var retenida = e.Base.Cliente.SendAsync(peticion);
        await compuerta.Retenida.WaitAsync(TimeSpan.FromSeconds(60));

        // Mientras tanto se confirma una corrección de planilla.
        await e.GuardarConExito("liga-a", p, Equipo(p, 1), Equipo(p, 2), CorreccionDeM1(p));
        compuerta.Anotar("correccion-confirmada");
        compuerta.Liberar();
        var (estado, durante) = await Leer(await retenida.WaitAsync(TimeSpan.FromSeconds(60)));
        compuerta.Anotar("respuesta-recibida");
        var despues = (await Leer(await e.Get(Posiciones("liga-a", p)))).Texto;

        // El orden de los eventos que se afirma.
        Assert.Equal(["puerto-retenido", "correccion-confirmada", "puerto-liberado", "respuesta-recibida"], compuerta.Eventos);
        Assert.Equal(HttpStatusCode.OK, estado);
        // La respuesta retenida es, byte a byte, la de antes de la corrección; una posterior es la del estado nuevo.
        Assert.Equal(antes, durante);
        Assert.NotEqual(antes, despues);

        // El estado nuevo, medido en otra liga que tiene la misma corrección aplicada sin ninguna compuerta de por medio.
        await e.CargarF6("liga-b", p, ReglasDeF6());
        await e.GuardarConExito("liga-b", p, Equipo(p, 1), Equipo(p, 2), CorreccionDeM1(p));
        Assert.Equal((await Leer(await e.Get(Posiciones("liga-b", p)))).Texto, despues);
    }

    // ---- T-20: la versión del reglamento bajo importaciones concurrentes ----

    private static JsonObject ReglasConVictoria(int victoria)
    {
        var reglas = ReglasDeF6();
        reglas["puntos"]!["victoria"] = victoria;
        return reglas;
    }

    private async Task<List<(int Version, string Estado, int Victoria)>> Versiones(string torneo)
    {
        await using var conexion = new NpgsqlConnection(db.CadenaSuperusuario);
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand($"""
            select r.version, r.estado, (r.contenido->'puntos'->>'victoria')::int
            from goalflow.reglamentos_torneo r join goalflow.torneos t on t.id = r.torneo_id
            where t.codigo = '{torneo}' order by r.version
            """, conexion);
        await using var lector = await comando.ExecuteReaderAsync();
        var filas = new List<(int, string, int)>();
        while (await lector.ReadAsync()) filas.Add((lector.GetInt32(0), lector.GetString(1), lector.GetInt32(2)));
        return filas;
    }

    private static int Lecturas(Interleavado control) =>
        control.Eventos.Count(x => x.StartsWith("lectura-importacion-", StringComparison.Ordinal));

    [Fact] // T-20 (a): las dos importaciones leyeron la versión 1; una gana y la otra recibe 409
    public async Task T20a_dos_importaciones_con_reglamentos_distintos_que_leyeron_la_version_1_dejan_un_200_un_409_y_dos_versiones()
    {
        var control = new Interleavado();
        await using var e = new EntornoDeEstadisticas(db, ajustar: control.Registrar());
        var p = Prefijo();
        await e.ImportarConExito("liga-a", p, CuerpoDeF6(p, ReglasDeF6()));
        var lecturasPrevias = Lecturas(control);
        control.Importacion.TrasLeer = Interleavado.Compuerta(abierta: false);

        var primera = e.Importar("liga-a", p, CuerpoDeF6(p, ReglasDeF6("Confirmado")));
        var segunda = e.Importar("liga-a", p, CuerpoDeF6(p, ReglasConVictoria(2)));
        await Esperar(() => Lecturas(control) >= lecturasPrevias + 2, "las dos lecturas de la versión 1");

        // El entrelazado que se afirma: las dos lecturas devolvieron y ninguna escritura empezó.
        Assert.Equal(lecturasPrevias + 2, Lecturas(control));
        var escriturasHastaAqui = control.Eventos.Count(x => x.StartsWith("escritura-importacion-", StringComparison.Ordinal));
        Assert.Equal(1, escriturasHastaAqui); // solo la de la preparación
        control.Importacion.TrasLeer.SetResult();

        var respuestas = await Task.WhenAll(primera, segunda);
        var resultados = await Task.WhenAll(respuestas.Select(Leer));

        Assert.DoesNotContain(resultados, r => (int)r.Estado >= 500);
        var ganadora = Assert.Single(resultados, r => r.Estado == HttpStatusCode.OK);
        var perdedora = Assert.Single(resultados, r => r.Estado == HttpStatusCode.Conflict);
        SinFugas(perdedora.Texto);
        using (var cuerpo = JsonDocument.Parse(perdedora.Texto))
            Assert.Equal("concurrencia", cuerpo.RootElement.GetProperty("conflictos")[0].GetProperty("tipo").GetString());

        // Exactamente 2 versiones: la 1 intacta y la de quien ganó; nada de la perdedora.
        var versiones = await Versiones(p);
        Assert.Equal([1, 2], versiones.Select(v => v.Version));
        Assert.Equal(("Provisional", 3), (versiones[0].Estado, versiones[0].Victoria));
        var gano = Array.IndexOf(respuestas.Select(r => r.StatusCode).ToArray(), HttpStatusCode.OK) == 0;
        Assert.Equal(gano ? ("Confirmado", 3) : ("Provisional", 2), (versiones[1].Estado, versiones[1].Victoria));
        Assert.NotNull(ganadora.Texto);
    }

    [Fact] // T-20 (b): la segunda lectura empieza después del commit de la primera; dos 200 con las versiones 2 y 3
    public async Task T20b_con_la_segunda_lectura_despues_del_commit_de_la_primera_hay_dos_200_y_las_versiones_2_y_3()
    {
        var control = new Interleavado();
        await using var e = new EntornoDeEstadisticas(db, ajustar: control.Registrar());
        var p = Prefijo();
        await e.ImportarConExito("liga-a", p, CuerpoDeF6(p, ReglasDeF6()));
        var previas = Lecturas(control);
        control.Importacion.AntesDeLeer = Interleavado.Compuerta(abierta: false);
        control.Importacion.RetenerLaLecturaNumero = previas + 2;

        var primera = e.Importar("liga-a", p, CuerpoDeF6(p, ReglasDeF6("Confirmado")));
        var segunda = e.Importar("liga-a", p, CuerpoDeF6(p, ReglasConVictoria(2)));

        // Una de las dos lee y escribe; la otra queda retenida ANTES de leer. Se espera a que la primera confirme.
        var primeraEnTerminar = await Task.WhenAny(primera, segunda).WaitAsync(TimeSpan.FromSeconds(60));
        Assert.Equal(HttpStatusCode.OK, (await primeraEnTerminar).StatusCode);
        Assert.Equal(previas + 1, Lecturas(control)); // la retenida todavía no leyó
        Assert.Equal(2, (await Versiones(p)).Count);  // y la primera ya confirmó la versión 2
        control.Importacion.AntesDeLeer.SetResult();

        var respuestas = await Task.WhenAll(primera, segunda);

        Assert.All(respuestas, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal([1, 2, 3], (await Versiones(p)).Select(v => v.Version));

        // El orden observado: la escritura de la primera ocurrió antes de la lectura de la segunda.
        var eventos = control.Eventos;
        var escrituraDeLaPrimera = eventos.ToList().FindIndex(x => x == $"escritura-importacion-{previas + 1}");
        var lecturaDeLaSegunda = eventos.ToList().FindIndex(x => x == $"lectura-importacion-{previas + 2}");
        Assert.True(escrituraDeLaPrimera >= 0 && lecturaDeLaSegunda > escrituraDeLaPrimera,
            $"Orden observado: {string.Join(", ", eventos)}");
    }
}
