using System.Net;
using System.Text.Json.Nodes;
using GoalFlow.Application.IA;
using GoalFlow.Application.Persistencia;
using GoalFlow.Application.Torneos.Contracts;
using GoalFlow.Domain.IA;
using Microsoft.Extensions.DependencyInjection;
using static GoalFlow.Tests.Isolation.EntornoDeLecturasPublicas;

namespace GoalFlow.Tests.Isolation;

/// <summary>
/// Spec 011 T-06 (c) (R-20, I-03): los hechos y las filas de resumen del detalle salen de <b>una sola</b> foto, aunque el resumen se
/// confirme después de leer los hechos y antes de leer los resúmenes. La compuerta retiene la petición justo después de
/// <c>LeerPorClaveAsync</c>; T-06 (a) y (b) retienen dentro de la lectura de resúmenes y por eso no cuentan transacciones.
/// </summary>
[Collection(ColeccionDePlanilla.Nombre)]
public class FotoUnicaEntreLecturasTests(BaseDeAislamiento db)
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly string[] EventosEsperados =
        ["lectura-de-hechos", "retenida", "resumen-confirmado", "liberada", "lectura-de-resumenes"];

    private sealed record Resultado(JsonNode Retenido, JsonNode Posterior, IReadOnlyList<string> Eventos, string Plantilla);

    private async Task<Resultado> Ejecutar(bool lecturaConfirmada)
    {
        var control = new ControlDeHechosDelDetalle();
        await using var e = new EntornoDeLecturasPublicas(db, control.Registrar(lecturaConfirmada));
        var (liga, tenant) = await e.CrearLigaConTenant();
        await e.E.ImportarConExito(liga, "T6C", TorneoMinimo("Torneo Seis C", (1, 1)));
        await e.E.GuardarConExito(liga, "T6C", "E1", "E2", new(0, "Jugado", new(2, 1), TarjetasInformadas: true, GolesSinAutor: new(2, 1)));

        var partido = (await e.PartidosDe(tenant, "T6C")).Single();
        EntradaCanonica entrada;
        Guid torneoId;
        int version;
        await using (var scope = e.ScopeDe(tenant))
        {
            await using var transaccion = await scope.ServiceProvider.GetRequiredService<IUnidadDeTrabajo>().IniciarAsync(Ct);
            var datos = (await scope.ServiceProvider.GetRequiredService<ILectorDePartidoParaResumen>().LeerAsync(partido.Id, Ct))!;
            (entrada, torneoId, version) = (EntradaCanonica.Construir(datos), datos.TorneoId, datos.Version);
        }

        var ruta = Detalle(liga, "T6C", 1, "E1", "E2");
        var peticion = new HttpRequestMessage(HttpMethod.Get, ruta);
        peticion.Headers.Add(ControlDeHechosDelDetalle.Encabezado, "1");
        var retenida = e.E.Base.Cliente.SendAsync(peticion);
        await control.Retenida.Task.WaitAsync(TimeSpan.FromSeconds(60));

        // Con los hechos ya leídos y antes de leer los resúmenes, otra transacción inserta y confirma la plantilla con la huella vigente.
        var plantilla = PlantillasDeResumen.Plantilla(entrada);
        await e.InsertarResumenes(tenant, new ResumenPartido(
            Guid.CreateVersion7(), tenant, torneoId, partido.Id, version, entrada.Huella(), OrigenDeResumen.Plantilla,
            EstadoDeResumen.PorVerificar, null, plantilla, null, null, null));
        control.Anotar("resumen-confirmado");
        control.Liberar();

        using var respuesta = await retenida.WaitAsync(TimeSpan.FromSeconds(60));
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var retenido = JsonNode.Parse(await respuesta.Content.ReadAsStringAsync())!;
        var posterior = await e.Cuerpo(ruta);
        return new Resultado(retenido, posterior, control.Eventos, plantilla);
    }

    private static string Hechos(JsonNode cuerpo)
    {
        var copia = JsonNode.Parse(cuerpo.ToJsonString())!.AsObject();
        copia.Remove("resumen");
        return copia.ToJsonString();
    }

    [Fact] // T-06 (c): una sola foto para los hechos y los resúmenes
    public async Task T06c_el_detalle_retenido_entre_sus_hechos_y_sus_resumenes_lee_una_sola_foto()
    {
        var r = await Ejecutar(lecturaConfirmada: false);

        Assert.Equal(EventosEsperados, r.Eventos);
        Assert.Null(r.Retenido["resumen"]);
        Assert.Equal(r.Plantilla, (string)r.Posterior["resumen"]!["texto"]!);
    }

    [Fact] // T-06 (c): control negativo con READ COMMITTED: el cuerpo retenido trae el texto
    public async Task T06c_control_negativo_con_READ_COMMITTED_el_cuerpo_retenido_si_trae_el_texto()
    {
        var r = await Ejecutar(lecturaConfirmada: true);

        Assert.Equal(EventosEsperados, r.Eventos);
        Assert.Equal(r.Plantilla, (string)r.Retenido["resumen"]!["texto"]!);
        Assert.Equal("Plantilla", (string)r.Retenido["resumen"]!["origen"]!);
    }

    [Fact] // T-06 (c): los hechos de las dos variantes son idénticos y coinciden con los de la petición posterior
    public async Task T06c_los_hechos_de_la_lectura_consistente_y_del_control_negativo_son_identicos()
    {
        var (a, b) = (await Ejecutar(lecturaConfirmada: false), await Ejecutar(lecturaConfirmada: true));

        Assert.Equal(Hechos(a.Retenido), Hechos(b.Retenido));
        Assert.Equal(Hechos(a.Retenido), Hechos(a.Posterior));
    }
}
