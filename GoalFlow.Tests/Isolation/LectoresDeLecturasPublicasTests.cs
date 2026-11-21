using System.Text.Json;
using GoalFlow.Application.IA;
using GoalFlow.Application.Persistencia;
using GoalFlow.Application.Torneos.Contracts;
using GoalFlow.Domain.IA;
using GoalFlow.Domain.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using static GoalFlow.Tests.Isolation.BaseDeAislamiento;
using static GoalFlow.Tests.Isolation.EntornoDeEstadisticas;
using static GoalFlow.Tests.Isolation.EntornoDeLecturasPublicas;

namespace GoalFlow.Tests.Isolation;

/// <summary>
/// Spec 011 commit 4, pruebas de puerto (handoff §6.3): <c>LeerPorClaveAsync</c> (R-30, R-15) y <c>ListarDelPartidoAsync</c> (R-15, T-05
/// integración), por el registro real de DI contra PostgreSQL 17 como goalflow_app. Los datos se cargan por la API real.
/// </summary>
[Collection(ColeccionDePlanilla.Nombre)]
public class LectoresDeLecturasPublicasTests(BaseDeAislamiento db)
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static string Json(DatosDePartidoParaResumen datos) => JsonSerializer.Serialize(datos);

    /// <summary>Una liga con T-02 (sin jugadores) y F6 (con goleadores, autogoles y tarjetas), cargadas por la API real.</summary>
    private static async Task<(string Liga, TenantId Tenant, string F6)> Preparar(EntornoDeLecturasPublicas e)
    {
        var (liga, tenant) = await e.CrearLigaConTenant();
        await e.CargarT02(liga);
        var f6 = Prefijo();
        await e.E.CargarF6(liga, f6, ReglasDeF6());
        return (liga, tenant, f6);
    }

    private static Task<ITransaccion> Lectura(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IUnidadDeTrabajo>().IniciarLecturaConsistenteAsync(Ct);

    [Fact] // R-30: la clave natural devuelve el partido correcto y los mismos datos que LeerAsync, por la misma proyección
    public async Task R30_LeerPorClave_devuelve_para_cada_partido_el_mismo_Datos_que_LeerAsync_y_su_PartidoId()
    {
        await using var e = new EntornoDeLecturasPublicas(db);
        var (_, tenant, f6) = await Preparar(e);
        var (t02, deF6) = (await e.PartidosDe(tenant, TorneoT02), await e.PartidosDe(tenant, f6));
        Assert.Equal((8, 15), (t02.Count, deF6.Count));

        await using var scope = e.ScopeDe(tenant);
        var lector = scope.ServiceProvider.GetRequiredService<ILectorDePartidoParaResumen>();
        await using var transaccion = await Lectura(scope);

        foreach (var (torneo, partidos) in new[] { (TorneoT02, t02), (f6, deF6) })
            foreach (var (id, local, visitante) in partidos)
            {
                var porClave = await lector.LeerPorClaveAsync(new ClaveNaturalDePartido(torneo, 1, local, visitante), Ct);
                var porId = await lector.LeerAsync(id, Ct);

                Assert.NotNull(porClave);
                Assert.Equal(id, porClave.PartidoId);
                Assert.NotNull(porId);
                // Los records comparan sus listas por referencia: se comparan los valores, serializados.
                Assert.Equal(Json(porId), Json(porClave.Datos));
            }
    }

    [Fact] // R-30: con cualquier parte inexistente, null
    public async Task R30_LeerPorClave_devuelve_null_con_una_clave_inexistente_invertida_o_que_solo_difiere_en_mayusculas()
    {
        await using var e = new EntornoDeLecturasPublicas(db);
        var (_, tenant, _) = await Preparar(e);
        await using var scope = e.ScopeDe(tenant);
        var lector = scope.ServiceProvider.GetRequiredService<ILectorDePartidoParaResumen>();
        await using var transaccion = await Lectura(scope);

        Assert.NotNull(await lector.LeerPorClaveAsync(new(TorneoT02, 1, "E1", "E2"), Ct)); // control positivo: la clave base existe
        foreach (var (clave, motivo) in new (ClaveNaturalDePartido, string)[]
        {
            (new("NO-EXISTE", 1, "E1", "E2"), "torneo inexistente"),
            (new(TorneoT02, 9, "E1", "E2"), "fase inexistente"),
            (new(TorneoT02, 1, "E1", "E8"), "partido inexistente"),
            (new(TorneoT02, 1, "E2", "E1"), "clave invertida de una fase de una vuelta"),
            (new("t2", 1, "E1", "E2"), "torneo en minúscula"),
            (new(TorneoT02, 1, "e1", "E2"), "local en minúscula"),
            (new(TorneoT02, 1, "E1", "e2"), "visitante en minúscula"),
            (new(TorneoT02, 1, "E5", "E6"), "«E5» no es «e5»"),
        })
            Assert.True(await lector.LeerPorClaveAsync(clave, Ct) is null, motivo);
    }

    [Fact] // R-30 + ADR-14: la clave de otra liga no existe en este tenant
    public async Task R30_LeerPorClave_no_ve_el_partido_de_otra_liga()
    {
        await using var e = new EntornoDeLecturasPublicas(db);
        await Preparar(e);

        await using var scope = e.ScopeDe(LigaA);
        var lector = scope.ServiceProvider.GetRequiredService<ILectorDePartidoParaResumen>();
        await using var transaccion = await Lectura(scope);

        Assert.Null(await lector.LeerPorClaveAsync(new(TorneoT02, 1, "E1", "E2"), Ct));
    }

    [Fact] // R-30: sin transacción, TransaccionRequeridaException
    public async Task R30_LeerPorClave_sin_transaccion_lanza_TransaccionRequerida()
    {
        await using var e = new EntornoDeLecturasPublicas(db);
        await using var scope = e.ScopeDe(LigaA);
        var lector = scope.ServiceProvider.GetRequiredService<ILectorDePartidoParaResumen>();

        await Assert.ThrowsAsync<TransaccionRequeridaException>(() => lector.LeerPorClaveAsync(new(TorneoT02, 1, "E1", "E2"), Ct));
    }

    [Fact] // R-15: sin transacción, TransaccionRequeridaException
    public async Task R15_ListarDelPartido_sin_transaccion_lanza_TransaccionRequerida()
    {
        await using var e = new EntornoDeLecturasPublicas(db);
        await using var scope = e.ScopeDe(LigaA);
        var almacen = scope.ServiceProvider.GetRequiredService<IAlmacenDeResumenes>();

        await Assert.ThrowsAsync<TransaccionRequeridaException>(() => almacen.ListarDelPartidoAsync(Guid.NewGuid(), Ct));
    }

    [Fact] // R-15 y T-05 (integración): un Borrador, una fila de huella vieja y una vigente: devuelve las tres, sin filtrar
    public async Task R15_ListarDelPartido_devuelve_las_tres_filas_de_un_partido_con_un_Borrador_una_huella_vieja_y_una_vigente()
    {
        await using var e = new EntornoDeLecturasPublicas(db);
        var (_, tenant, _) = await Preparar(e);
        var partido = (await e.PartidosDe(tenant, TorneoT02)).Single(p => (p.Local, p.Visitante) == ("E3", "E4"));
        var otro = (await e.PartidosDe(tenant, TorneoT02)).Single(p => (p.Local, p.Visitante) == ("E1", "E2"));

        string vigente;
        Guid torneoId;
        await using (var scope = e.ScopeDe(tenant))
        {
            await using var transaccion = await Lectura(scope);
            var datos = await scope.ServiceProvider.GetRequiredService<ILectorDePartidoParaResumen>().LeerAsync(partido.Id, Ct);
            vigente = EntradaCanonica.Construir(datos!).Huella();
            torneoId = datos!.TorneoId;
        }

        ResumenPartido Fila(Guid partidoId, string huella, EstadoDeResumen estado, MotivoDeBorrador? motivo, string texto) =>
            new(Guid.CreateVersion7(), tenant, torneoId, partidoId, 1, huella, OrigenDeResumen.Plantilla, estado, motivo, texto, null, null, null);
        await e.InsertarResumenes(
            tenant,
            Fila(partido.Id, new string('b', 64), EstadoDeResumen.Borrador, MotivoDeBorrador.Presupuesto, "Texto borrador"),
            Fila(partido.Id, new string('a', 64), EstadoDeResumen.PorVerificar, null, "Texto de una huella vieja"),
            Fila(partido.Id, vigente, EstadoDeResumen.PorVerificar, null, "Texto de la huella vigente"),
            Fila(otro.Id, new string('c', 64), EstadoDeResumen.PorVerificar, null, "Texto de otro partido"));

        await using var lectura = e.ScopeDe(tenant);
        await using var tx = await Lectura(lectura);
        var filas = await lectura.ServiceProvider.GetRequiredService<IAlmacenDeResumenes>().ListarDelPartidoAsync(partido.Id, Ct);

        Assert.Equal(
            ["Texto borrador", "Texto de la huella vigente", "Texto de una huella vieja"],
            filas.Select(f => f.Texto).Order(StringComparer.Ordinal).ToArray());
        Assert.All(filas, f => Assert.Equal(partido.Id, f.PartidoId));
        Assert.Empty(await lectura.ServiceProvider.GetRequiredService<IAlmacenDeResumenes>().ListarDelPartidoAsync(Guid.NewGuid(), Ct));
    }

    [Fact] // R-15 + ADR-14: el scope de otra liga no ve las filas de este partido
    public async Task R15_ListarDelPartido_no_ve_las_filas_de_otra_liga()
    {
        await using var e = new EntornoDeLecturasPublicas(db);
        var (_, tenant, _) = await Preparar(e);
        var partido = (await e.PartidosDe(tenant, TorneoT02)).First();
        Guid torneoId;
        await using (var scope = e.ScopeDe(tenant))
        {
            await using var transaccion = await Lectura(scope);
            torneoId = (await scope.ServiceProvider.GetRequiredService<ILectorDePartidoParaResumen>().LeerAsync(partido.Id, Ct))!.TorneoId;
        }

        await e.InsertarResumenes(tenant, new ResumenPartido(
            Guid.CreateVersion7(), tenant, torneoId, partido.Id, 1, new string('d', 64), OrigenDeResumen.Plantilla,
            EstadoDeResumen.PorVerificar, null, "Texto propio", null, null, null));

        await using var ajeno = e.ScopeDe(LigaA);
        await using var tx = await Lectura(ajeno);
        Assert.Empty(await ajeno.ServiceProvider.GetRequiredService<IAlmacenDeResumenes>().ListarDelPartidoAsync(partido.Id, Ct));
    }
}
