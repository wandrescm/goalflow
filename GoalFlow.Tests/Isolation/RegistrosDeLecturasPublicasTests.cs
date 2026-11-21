using System.Net;
using System.Text.Json.Nodes;
using GoalFlow.Application.IA;
using GoalFlow.Application.Persistencia;
using GoalFlow.Application.Torneos.Contracts;
using GoalFlow.Domain.IA;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static GoalFlow.Tests.Isolation.BaseDeAislamiento;
using static GoalFlow.Tests.Isolation.EntornoDeLecturasPublicas;

namespace GoalFlow.Tests.Isolation;

/// <summary>
/// Spec 011 T-11 (R-26): los registros de las tres rutas llevan tenant, ruta, código de torneo, orden de fase, conteos y, en el detalle,
/// si se publicó un resumen (booleano); nunca un nombre, el texto del resumen, la huella, la IP del cliente ni el <c>sub</c>.
/// </summary>
[Collection(ColeccionDePlanilla.Nombre)]
public class RegistrosDeLecturasPublicasTests(BaseDeAislamiento db)
{
    private const string IpDePrueba = "203.0.113.77";
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static Task<HttpResponseMessage> Get(EntornoDeLecturasPublicas e, string ruta)
    {
        var peticion = new HttpRequestMessage(HttpMethod.Get, ruta);
        peticion.Headers.Add("CF-Connecting-IP", IpDePrueba);
        return e.E.Base.Cliente.SendAsync(peticion);
    }

    [Fact] // T-11: verde inicial para la parte negativa; un mutante que registre un nombre la pondrá en rojo (Fase B)
    public async Task T11_los_registros_llevan_los_valores_esperados_y_ningun_nombre_texto_huella_ip_ni_sub()
    {
        var logs = new ProveedorDeLogsDeCaptura();
        await using var e = new EntornoDeLecturasPublicas(db, b => b.ConfigureTestServices(s => s.AddSingleton<ILoggerProvider>(logs)));
        var (liga, tenant) = await e.CrearLigaConTenant("Liga Registro");
        var sufijo = $"zq{Guid.NewGuid():N}"; // nombres únicos: la búsqueda en los registros no da falsos negativos

        var cuerpo = TorneoMinimo($"Torneo Zeta {sufijo}", (1, 1));
        foreach (var equipo in cuerpo["equipos"]!.AsArray().Select(x => x!.AsObject()))
        {
            equipo["nombre"] = $"Equipo Zeta {equipo["nombre"]} {sufijo}";
            equipo["jugadores"] = new JsonArray(new JsonObject { ["codigo"] = $"J{equipo["codigo"]}", ["nombre"] = $"Jugador Zeta {equipo["codigo"]} {sufijo}" });
        }

        await e.E.ImportarConExito(liga, "T11", cuerpo);
        await e.E.GuardarConExito(liga, "T11", "E1", "E2", new(0, "Jugado", new(1, 0), TarjetasInformadas: true, Goles: [new("JE1", 1)]));

        var partido = (await e.PartidosDe(tenant, "T11")).Single();
        EntradaCanonica entrada;
        Guid torneoId;
        await using (var scope = e.ScopeDe(tenant))
        {
            await using var transaccion = await scope.ServiceProvider.GetRequiredService<IUnidadDeTrabajo>().IniciarAsync(Ct);
            var datos = (await scope.ServiceProvider.GetRequiredService<ILectorDePartidoParaResumen>().LeerAsync(partido.Id, Ct))!;
            (entrada, torneoId) = (EntradaCanonica.Construir(datos), datos.TorneoId);
        }

        var texto = PlantillasDeResumen.Plantilla(entrada);
        var huella = entrada.Huella();
        await e.InsertarResumenes(tenant, new ResumenPartido(
            Guid.CreateVersion7(), tenant, torneoId, partido.Id, 1, huella, OrigenDeResumen.Plantilla, EstadoDeResumen.PorVerificar, null, texto, null, null, null));

        // Las tres rutas, incluido el detalle con un resumen publicado y otro detalle sin él (404 de un partido inexistente no cuenta).
        Assert.Equal(HttpStatusCode.OK, (await Get(e, Catalogo(liga))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Get(e, Partidos(liga, "T11"))).StatusCode);
        var detalle = await Get(e, Detalle(liga, "T11", 1, "E1", "E2"));
        Assert.Equal(HttpStatusCode.OK, detalle.StatusCode);
        Assert.Contains(texto, await detalle.Content.ReadAsStringAsync(), StringComparison.Ordinal); // control positivo: se publicó

        var registros = logs.Registros;
        var liga_ = tenant.Value.ToString();

        // Los valores esperados de cada ruta.
        var delCatalogo = registros.Where(r => r.Valores.GetValueOrDefault("Ruta") == "torneos").ToList();
        var delaLista = registros.Where(r => r.Valores.GetValueOrDefault("Ruta") == "partidos").ToList();
        var delDetalle = registros.Where(r => r.Valores.GetValueOrDefault("Ruta") == "detalle").ToList();
        var catalogo = Assert.Single(delCatalogo);
        var lista = Assert.Single(delaLista);
        var detalleLog = Assert.Single(delDetalle);
        Assert.Equal((liga_, "1", "1"), (catalogo.Valores["Liga"], catalogo.Valores["Torneos"], catalogo.Valores["Fases"]));
        Assert.Equal((liga_, "T11", "1", "1", "True"),
            (lista.Valores["Liga"], lista.Valores["Torneo"], lista.Valores["Fase"], lista.Valores["Partidos"], lista.Valores["Encontrado"]));
        Assert.Equal((liga_, "T11", "1", "1", "0", "True", "True"),
            (detalleLog.Valores["Liga"], detalleLog.Valores["Torneo"], detalleLog.Valores["Fase"], detalleLog.Valores["Goles"],
                detalleLog.Valores["Tarjetas"], detalleLog.Valores["Encontrado"], detalleLog.Valores["ResumenPublicado"]));

        // Nada de lo prohibido en ningún registro, de ninguna categoría.
        var prohibidos = new[] { sufijo, "Equipo Zeta", "Jugador Zeta", texto, huella, IpDePrueba, UsuarioU1.ToString() };
        Assert.True(registros.Count > 6, "Se esperaba capturar los registros de todo el escenario");
        foreach (var registro in registros)
            foreach (var prohibido in prohibidos)
                Assert.False(registro.TodoElTexto.Contains(prohibido, StringComparison.OrdinalIgnoreCase),
                    $"El registro de {registro.Categoria} contiene «{prohibido}»: {registro.Mensaje}");
    }
}
