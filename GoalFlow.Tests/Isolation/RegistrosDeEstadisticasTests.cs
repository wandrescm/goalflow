using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static GoalFlow.Tests.Isolation.BaseDeAislamiento;
using static GoalFlow.Tests.Isolation.EntornoDeEstadisticas;

namespace GoalFlow.Tests.Isolation;

/// <summary>Un mensaje de log capturado: su categoría, su texto formateado, sus valores estructurados y su excepción, si la hubo.</summary>
public sealed record RegistroCapturado(
    string Categoria, LogLevel Nivel, string Mensaje, IReadOnlyDictionary<string, string> Valores, string? Excepcion)
{
    /// <summary>Todo lo que el registro dejó escrito, para buscar en él un texto que no debe aparecer.</summary>
    public string TodoElTexto => string.Join(" | ", [Mensaje, .. Valores.Select(v => $"{v.Key}={v.Value}"), Excepcion ?? string.Empty]);
}

/// <summary>
/// Proveedor de logs de prueba (Spec 009 T-23): captura cada mensaje formateado y cada valor estructurado
/// (<c>IReadOnlyList&lt;KeyValuePair&lt;string, object?&gt;&gt;</c>), de todas las categorías. Sin paquetes nuevos.
/// </summary>
public sealed class ProveedorDeLogsDeCaptura : ILoggerProvider
{
    private readonly List<RegistroCapturado> _registros = [];

    public IReadOnlyList<RegistroCapturado> Registros
    {
        get
        {
            lock (_registros) return [.. _registros];
        }
    }

    public ILogger CreateLogger(string categoryName) => new Registrador(categoryName, this);

    public void Dispose()
    {
    }

    private void Agregar(RegistroCapturado registro)
    {
        lock (_registros) _registros.Add(registro);
    }

    private sealed class Registrador(string categoria, ProveedorDeLogsDeCaptura proveedor) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var valores = new Dictionary<string, string>(StringComparer.Ordinal);
            if (state is IReadOnlyList<KeyValuePair<string, object?>> estructurado)
                foreach (var (clave, valor) in estructurado) valores[clave] = valor?.ToString() ?? string.Empty;

            proveedor.Agregar(new RegistroCapturado(categoria, logLevel, formatter(state, exception), valores, exception?.ToString()));
        }
    }
}

/// <summary>Spec 009 T-23 (R-35, R-10): los registros de las consultas y de la importación no llevan nombres ni el <c>sub</c>.</summary>
[Collection(ColeccionDePlanilla.Nombre)]
public class RegistrosDeEstadisticasTests(BaseDeAislamiento db)
{
    private static readonly string[] ValoresEsperados = ["Liga", "Torneo", "Endpoint", "VersionDelReglamento", "Filas", "Resultado"];

    /// <summary>El cuerpo de la importación de F6 con nombres que no pueden aparecer por casualidad en otro texto del log.</summary>
    private static JsonObject CuerpoConNombresUnicos(string p, string sufijo, JsonObject? reglamento)
    {
        var cuerpo = CuerpoDeF6(p, reglamento);
        foreach (var equipo in cuerpo["equipos"]!.AsArray().Select(x => x!.AsObject()))
        {
            equipo["nombre"] = $"{equipo["nombre"]} {sufijo}";
            foreach (var jugador in equipo["jugadores"]!.AsArray().Select(x => x!.AsObject()))
                jugador["nombre"] = $"{jugador["nombre"]} {sufijo}";
        }

        return cuerpo;
    }

    [Fact] // T-23: verde inicial, protege código existente; su rojo lo dará un mutante de la Fase B
    public async Task T23_los_registros_de_las_consultas_y_de_la_importacion_llevan_los_valores_esperados_y_ningun_nombre_ni_el_sub()
    {
        var logs = new ProveedorDeLogsDeCaptura();
        await using var e = new EntornoDeEstadisticas(db, ajustar: b => b.ConfigureTestServices(s => s.AddSingleton<ILoggerProvider>(logs)));
        var (p, sinReglas) = (Prefijo(), Prefijo());
        var sufijo = $"zq{Guid.NewGuid():N}";

        // F6 con su reglamento (como T-11) y un torneo igual, sin reglamento, para el 409.
        await e.ImportarConExito("liga-a", p, CuerpoConNombresUnicos(p, sufijo, ReglasDeF6()));
        foreach (var accion in e.PlanillasDeF6("liga-a", p)) await accion();
        await e.ImportarConExito("liga-a", sinReglas, CuerpoConNombresUnicos(sinReglas, sufijo, reglamento: null));
        foreach (var accion in e.PlanillasDeF6("liga-a", sinReglas)) await accion();

        foreach (var ruta in new[] { Posiciones("liga-a", p), Goleadores("liga-a", p), Valla("liga-a", p) })
            Assert.Equal(HttpStatusCode.OK, (await e.Get(ruta)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await e.Get(Posiciones("liga-a", sinReglas))).StatusCode);

        var registros = logs.Registros;

        // 1. Al menos un mensaje de estadísticas por endpoint, con los seis valores.
        var deEstadisticas = registros.Where(r => r.Categoria.StartsWith("GoalFlow.Application.Estadisticas.", StringComparison.Ordinal)).ToList();
        foreach (var endpoint in new[] { "posiciones", "goleadores", "valla" })
        {
            var delEndpoint = deEstadisticas.Where(r => r.Valores.GetValueOrDefault("Endpoint") == endpoint).ToList();
            Assert.NotEmpty(delEndpoint);
            Assert.All(delEndpoint, r => Assert.All(ValoresEsperados, clave => Assert.True(r.Valores.ContainsKey(clave), $"Falta {clave} en: {r.Mensaje}")));
        }

        var exitosos = deEstadisticas.Where(r => r.Valores["Torneo"] == p).ToList();
        Assert.Equal(3, exitosos.Count);
        Assert.All(exitosos, r =>
        {
            Assert.Equal("1", r.Valores["VersionDelReglamento"]);
            Assert.Equal("ok", r.Valores["Resultado"]);
        });
        var pendiente = Assert.Single(deEstadisticas, r => r.Valores["Torneo"] == sinReglas);
        Assert.Equal(("posiciones", "ninguno", "0", "configuracion_pendiente"),
            (pendiente.Valores["Endpoint"], pendiente.Valores["VersionDelReglamento"], pendiente.Valores["Filas"], pendiente.Valores["Resultado"]));

        // 3. El log de la importación existe y lleva la versión y el estado del reglamento, nunca el sub (R-10).
        Assert.Contains(registros, r => r.Categoria == "GoalFlow.Application.Torneos.ImportacionDeTorneo" && r.Valores.ContainsKey("VersionDelReglamento"));

        // 2. Ningún registro, de ninguna categoría, contiene un nombre de equipo o de jugador de F6 ni el sub del Admin.
        var prohibidos = new List<string> { sufijo, UsuarioU1.ToString() };
        prohibidos.AddRange(Enumerable.Range(1, 6).Select(i => $"Equipo E{i}"));
        prohibidos.AddRange(new[] { "11", "12", "21", "22", "31", "41", "51", "61", "62" }.Select(n => $"Jugador P{n}"));
        Assert.True(registros.Count > 10, "Se esperaba capturar los registros de todo el escenario");
        foreach (var registro in registros)
            foreach (var texto in prohibidos)
                Assert.False(registro.TodoElTexto.Contains(texto, StringComparison.OrdinalIgnoreCase),
                    $"El registro de {registro.Categoria} contiene «{texto}»: {registro.Mensaje}");
    }
}
