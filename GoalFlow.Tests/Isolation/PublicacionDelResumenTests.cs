using System.Net;
using System.Text.Json.Nodes;
using GoalFlow.Application.IA;
using GoalFlow.Application.IA.Verificacion;
using GoalFlow.Application.Persistencia;
using GoalFlow.Application.Torneos.Contracts;
using GoalFlow.Domain.IA;
using GoalFlow.Domain.Tenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using static GoalFlow.Tests.Isolation.BaseDeAislamiento;
using static GoalFlow.Tests.Isolation.EntornoDeLecturasPublicas;

namespace GoalFlow.Tests.Isolation;

/// <summary>
/// Spec 011 commit 6: el resumen publicado en el detalle (R-14 a R-19) por la API real contra PostgreSQL 17 como goalflow_app.
/// Toda publicación pasa por <see cref="SelectorDePublicacion"/>; aquí se arman las situaciones almacenadas y se compara el cuerpo.
/// </summary>
[Collection(ColeccionDePlanilla.Nombre)]
public class PublicacionDelResumenTests(BaseDeAislamiento db)
{
    private const string Torneo = "T4";
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static JsonObject CuerpoDeT4() => TorneoMinimo("Torneo Cuatro", (1, 1));

    private static PlanillaJ Planilla() => new(0, "Jugado", new(2, 1), TarjetasInformadas: true, GolesSinAutor: new(2, 1));

    /// <summary>Una liga nueva con el partido E1–E2 de T4 jugado 2–1. Las cinco situaciones de T-04 usan los mismos hechos.</summary>
    private static async Task<Situacion> Preparar(EntornoDeLecturasPublicas e)
    {
        var (liga, tenant) = await e.CrearLigaConTenant();
        await e.E.ImportarConExito(liga, Torneo, CuerpoDeT4());
        await e.E.GuardarConExito(liga, Torneo, "E1", "E2", Planilla());
        return await Situacion.De(e, liga, tenant, Torneo);
    }

    private sealed record Situacion(string Liga, TenantId Tenant, Guid PartidoId, DatosDePartidoParaResumen Datos, EntradaCanonica Entrada)
    {
        public string Huella => Entrada.Huella();

        public string Ruta => Detalle(Liga, Torneo, 1, "E1", "E2");

        public static async Task<Situacion> De(EntornoDeLecturasPublicas e, string liga, TenantId tenant, string torneo)
        {
            var id = (await e.PartidosDe(tenant, torneo)).Single().Id;
            await using var scope = e.ScopeDe(tenant);
            await using var transaccion = await scope.ServiceProvider.GetRequiredService<IUnidadDeTrabajo>().IniciarLecturaConsistenteAsync(Ct);
            var datos = (await scope.ServiceProvider.GetRequiredService<ILectorDePartidoParaResumen>().LeerAsync(id, Ct))!;
            return new Situacion(liga, tenant, id, datos, EntradaCanonica.Construir(datos));
        }

        public string Plantilla => PlantillasDeResumen.Plantilla(Entrada);

        public ResumenPartido Fila(
            string huella, OrigenDeResumen origen, EstadoDeResumen estado, MotivoDeBorrador? motivo, string texto, Guid? llamada = null) =>
            new(Guid.CreateVersion7(), Tenant, Datos.TorneoId, PartidoId, Datos.Version, huella, origen, estado, motivo, texto,
                origen == OrigenDeResumen.Modelo ? "modelo-de-prueba" : null, origen == OrigenDeResumen.Modelo ? "v-de-prueba" : null, llamada);
    }

    private static async Task<JsonNode> LeerDetalle(EntornoDeLecturasPublicas e, Situacion s) => await e.Cuerpo(s.Ruta);

    [Fact] // T-04 (a) a (d): cuatro situaciones sin publicación dan cuerpos idénticos byte a byte, con resumen null
    public async Task T04_sin_fila_con_borrador_con_huella_vieja_o_con_texto_rechazado_los_cuerpos_son_identicos_y_sin_resumen()
    {
        await using var e = new EntornoDeLecturasPublicas(db);
        var (a, b, c, d) = (await Preparar(e), await Preparar(e), await Preparar(e), await Preparar(e));
        var rechazado = d.Plantilla + " cancha";
        Assert.False(VerificadorDeResumen.Verificar(rechazado, d.Entrada).Aprobado, "El texto de (d) debía ser rechazado por el verificador");
        Assert.True(VerificadorDeResumen.Verificar(d.Plantilla, d.Entrada).Aprobado, "Control positivo: la plantilla sin la palabra es aprobada");

        await e.InsertarResumenes(b.Tenant, b.Fila(b.Huella, OrigenDeResumen.Plantilla, EstadoDeResumen.Borrador, MotivoDeBorrador.Presupuesto, "Borrador con la huella vigente"));
        await e.InsertarResumenes(c.Tenant, c.Fila(new string('a', 64), OrigenDeResumen.Plantilla, EstadoDeResumen.PorVerificar, null, c.Plantilla));
        await e.InsertarResumenes(d.Tenant, d.Fila(d.Huella, OrigenDeResumen.Plantilla, EstadoDeResumen.PorVerificar, null, rechazado));

        var textos = new List<string>();
        foreach (var s in new[] { a, b, c, d }) textos.Add(await e.Texto(s.Ruta));

        Assert.All(textos, t => Assert.Contains("\"resumen\":null", t, StringComparison.Ordinal));
        Assert.All(textos.Skip(1), t => Assert.Equal(textos[0], t));
        // R-17: ningún cuerpo revela filas, estados, motivos ni huellas.
        Assert.All(textos, SinFugas);
        Assert.All(textos, t => Assert.DoesNotContain("cancha", t, StringComparison.Ordinal));
    }

    [Fact] // T-04 (e): una plantilla PorVerificar con la huella vigente y texto aprobado se publica
    public async Task T04_una_plantilla_aprobada_con_la_huella_vigente_se_publica_con_origen_Plantilla()
    {
        await using var e = new EntornoDeLecturasPublicas(db);
        var s = await Preparar(e);
        await e.InsertarResumenes(s.Tenant, s.Fila(s.Huella, OrigenDeResumen.Plantilla, EstadoDeResumen.PorVerificar, null, s.Plantilla));

        var cuerpo = await LeerDetalle(e, s);

        Assert.Equal(s.Plantilla, (string)cuerpo["resumen"]!["texto"]!);
        Assert.Equal("Plantilla", (string)cuerpo["resumen"]!["origen"]!);
        Assert.Equal(["texto", "origen"], cuerpo["resumen"]!.AsObject().Select(p => p.Key).ToArray());
        SinFugas(cuerpo.ToJsonString());
    }

    [Fact] // T-04: una fila del Modelo aprobada se publica con origen Modelo
    public async Task T04_una_fila_del_Modelo_aprobada_se_publica_con_origen_Modelo()
    {
        await using var e = new EntornoDeLecturasPublicas(db);
        var s = await Preparar(e);
        var llamada = new LlamadaIa(Guid.CreateVersion7(), s.Tenant, s.Datos.TorneoId, s.PartidoId, s.Datos.Version, s.Huella, 1, 1, "2026-11", 1000);
        await using (var scope = e.ScopeDe(s.Tenant))
        {
            await using var transaccion = await scope.ServiceProvider.GetRequiredService<IUnidadDeTrabajo>().IniciarAsync(Ct);
            await scope.ServiceProvider.GetRequiredService<IAlmacenDeLlamadas>().InsertarReservadaAsync(llamada, Ct);
            Assert.True(await scope.ServiceProvider.GetRequiredService<IAlmacenDeResumenes>().InsertarAsync(
                s.Fila(s.Huella, OrigenDeResumen.Modelo, EstadoDeResumen.PorVerificar, null, s.Plantilla, llamada.Id), Ct));
            await transaccion.ConfirmarAsync(Ct);
        }

        var cuerpo = await LeerDetalle(e, s);

        Assert.Equal(s.Plantilla, (string)cuerpo["resumen"]!["texto"]!);
        Assert.Equal("Modelo", (string)cuerpo["resumen"]!["origen"]!);
        SinFugas(cuerpo.ToJsonString());
    }

    [Fact] // T-07, R-25, C-14: dos ligas con huellas idénticas; solo la que guardó el resumen lo publica
    public async Task T07_dos_ligas_con_la_misma_huella_no_comparten_resumenes_ni_filas()
    {
        await using var e = new EntornoDeLecturasPublicas(db);
        var (p, soloB) = (EntornoDeEstadisticas.Prefijo(), $"SOLO-{EntornoDeEstadisticas.Prefijo()}");
        foreach (var liga in new[] { "liga-a", "liga-b" })
        {
            await e.E.ImportarConExito(liga, p, CuerpoDeT4());
            await e.E.GuardarConExito(liga, p, "E1", "E2", Planilla());
        }

        var (a, b) = (await Situacion.De(e, "liga-a", LigaA, p), await Situacion.De(e, "liga-b", LigaB, p));
        // Si las huellas no son iguales, el caso no prueba lo que dice (handoff §6.2, T-07): se detiene.
        Assert.Equal(a.Huella, b.Huella);

        await e.InsertarResumenes(LigaB, b.Fila(b.Huella, OrigenDeResumen.Plantilla, EstadoDeResumen.PorVerificar, null, b.Plantilla));
        await e.E.ImportarConExito("liga-b", soloB, CuerpoDeT4());

        var (deA, deB) = (await e.Cuerpo(Detalle("liga-a", p, 1, "E1", "E2")), await e.Cuerpo(Detalle("liga-b", p, 1, "E1", "E2")));
        Assert.Null(deA["resumen"]);
        Assert.Equal(b.Plantilla, (string)deB["resumen"]!["texto"]!);

        // El resto del cuerpo es idéntico: solo difiere el resumen.
        deA.AsObject().Remove("resumen");
        deB.AsObject().Remove("resumen");
        Assert.Equal(deA.ToJsonString(), deB.ToJsonString());

        // El catálogo y la lista de cada liga muestran solo lo suyo; el torneo de B es 404 en A.
        Assert.Equal(HttpStatusCode.OK, (await e.Get(Partidos("liga-b", soloB))).StatusCode);
        foreach (var ruta in new[] { Partidos("liga-a", soloB), Detalle("liga-a", soloB, 1, "E1", "E2") })
            Assert.Equal(HttpStatusCode.NotFound, (await e.Get(ruta)).StatusCode);
        var catalogoA = (await e.Cuerpo(Catalogo("liga-a")))["torneos"]!.AsArray().Select(t => (string)t!["codigo"]!).ToArray();
        var catalogoB = (await e.Cuerpo(Catalogo("liga-b")))["torneos"]!.AsArray().Select(t => (string)t!["codigo"]!).ToArray();
        Assert.Contains(p, catalogoA);
        Assert.DoesNotContain(soloB, catalogoA);
        Assert.Contains(soloB, catalogoB);
    }

    [Fact] // T-09: sin IA, las rutas se resuelven, publican lo almacenado y no escriben nada ni llaman al proveedor
    public async Task T09_con_la_configuracion_versionada_las_rutas_responden_publican_y_no_escriben_nada()
    {
        await using var e = new EntornoDeLecturasPublicas(db); // el anfitrión real: ValidateOnBuild de Program.cs y sin ninguna clave IA:*
        var configuracion = e.E.Services.GetRequiredService<IConfiguration>();
        Assert.NotEqual("true", configuracion["IA:Habilitada"]);
        Assert.Null(configuracion["IA:ClaveApi"]);
        Assert.Null(e.E.Services.GetService<IGeneradorDeTexto>()); // ningún generador registrado: nada puede llegar al proveedor

        var s = await Preparar(e);
        Assert.Null((await LeerDetalle(e, s))["resumen"]);
        await e.InsertarResumenes(s.Tenant, s.Fila(s.Huella, OrigenDeResumen.Plantilla, EstadoDeResumen.PorVerificar, null, s.Plantilla));

        var antes = await e.E.Base.Foto();
        Assert.Equal(HttpStatusCode.OK, (await e.Get(Catalogo(s.Liga))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await e.Get(Partidos(s.Liga, Torneo))).StatusCode);
        Assert.Equal(s.Plantilla, (string)(await LeerDetalle(e, s))["resumen"]!["texto"]!);
        var despues = await e.E.Base.Foto();

        EntornoDePlanilla.FotosIguales(antes, despues);
    }
}
