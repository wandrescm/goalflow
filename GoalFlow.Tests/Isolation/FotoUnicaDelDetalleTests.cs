using System.Net;
using System.Text.Json.Nodes;
using GoalFlow.Application.IA;
using GoalFlow.Application.Persistencia;
using GoalFlow.Application.Torneos.Contracts;
using GoalFlow.Domain.IA;
using GoalFlow.Domain.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using static GoalFlow.Tests.Isolation.EntornoDeLecturasPublicas;

namespace GoalFlow.Tests.Isolation;

/// <summary>
/// Spec 011 T-06 (R-20, I-02, I-03, C-04), con compuertas (Spec 006 C-24): el detalle retenido cuando empieza su lectura de resúmenes
/// no ve el resumen confirmado entretanto (<c>REPEATABLE READ</c>); el control negativo, con <c>READ COMMITTED</c>, sí lo ve. Cada
/// prueba afirma el orden de los eventos observados. No afirma que <c>READ COMMITTED</c> publique un texto incoherente (C-04).
/// </summary>
[Collection(ColeccionDePlanilla.Nombre)]
public class FotoUnicaDelDetalleTests(BaseDeAislamiento db)
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly string[] EventosEsperados =
        ["lectura-de-hechos", "lectura-de-resumenes-iniciada", "resumen-confirmado", "liberada"];

    private sealed record Resultado(JsonNode Retenido, JsonNode Posterior, IReadOnlyList<string> Eventos, string Plantilla);

    /// <summary>M es <c>Jugado</c> v1 sin resúmenes; sale el detalle, se retiene, se confirma la plantilla <c>PorVerificar</c> con la huella vigente y se libera.</summary>
    private async Task<Resultado> Ejecutar(bool lecturaConfirmada)
    {
        var control = new ControlDeLecturaDelDetalle();
        await using var e = new EntornoDeLecturasPublicas(db, control.Registrar(lecturaConfirmada));
        var (liga, tenant) = await e.CrearLigaConTenant();
        await e.E.ImportarConExito(liga, "T6", TorneoMinimo("Torneo Seis", (1, 1)));
        await e.E.GuardarConExito(liga, "T6", "E1", "E2", new(0, "Jugado", new(2, 1), TarjetasInformadas: true, GolesSinAutor: new(2, 1)));

        var partido = (await e.PartidosDe(tenant, "T6")).Single();
        EntradaCanonica entrada;
        Guid torneoId;
        int version;
        await using (var scope = e.ScopeDe(tenant))
        {
            await using var transaccion = await scope.ServiceProvider.GetRequiredService<IUnidadDeTrabajo>().IniciarAsync(Ct);
            var datos = (await scope.ServiceProvider.GetRequiredService<ILectorDePartidoParaResumen>().LeerAsync(partido.Id, Ct))!;
            (entrada, torneoId, version) = (EntradaCanonica.Construir(datos), datos.TorneoId, datos.Version);
        }

        var ruta = Detalle(liga, "T6", 1, "E1", "E2");
        var peticion = new HttpRequestMessage(HttpMethod.Get, ruta);
        peticion.Headers.Add(ControlDeLecturaDelDetalle.Encabezado, "1");
        var retenida = e.E.Base.Cliente.SendAsync(peticion);
        await control.Retenida.Task.WaitAsync(TimeSpan.FromSeconds(60));

        // Mientras la petición espera, otra transacción inserta y confirma la plantilla con la huella vigente de M.
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

    [Fact] // T-06 (a): con la unidad de trabajo real, el cuerpo retenido no trae el resumen y una petición posterior sí
    public async Task T06a_el_detalle_retenido_lee_una_sola_foto_y_no_ve_el_resumen_confirmado_entretanto()
    {
        var r = await Ejecutar(lecturaConfirmada: false);

        Assert.Equal(EventosEsperados, r.Eventos);
        Assert.Null(r.Retenido["resumen"]);
        Assert.Equal(r.Plantilla, (string)r.Posterior["resumen"]!["texto"]!);
    }

    [Fact] // T-06 (b): control negativo, la misma secuencia con READ COMMITTED: el cuerpo retenido trae el texto
    public async Task T06b_control_negativo_con_READ_COMMITTED_el_cuerpo_retenido_si_trae_el_texto()
    {
        var r = await Ejecutar(lecturaConfirmada: true);

        Assert.Equal(EventosEsperados, r.Eventos);
        Assert.Equal(r.Plantilla, (string)r.Retenido["resumen"]!["texto"]!); // si no, la prueba no distingue los niveles: se detiene
        Assert.Equal("Plantilla", (string)r.Retenido["resumen"]!["origen"]!);
    }

    [Fact] // T-06: los hechos de (a) y de (b) son idénticos
    public async Task T06_los_hechos_de_la_lectura_consistente_y_del_control_negativo_son_identicos()
    {
        var (a, b) = (await Ejecutar(lecturaConfirmada: false), await Ejecutar(lecturaConfirmada: true));

        Assert.Equal(Hechos(a.Retenido), Hechos(b.Retenido));
        Assert.Equal(Hechos(a.Retenido), Hechos(a.Posterior));
    }
}
