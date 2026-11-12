using GoalFlow.Application.Mensajeria;
using GoalFlow.Application.Persistencia;
using GoalFlow.Application.Torneos.Contracts;
using GoalFlow.Infrastructure.Mensajeria;
using GoalFlow.Infrastructure.Persistencia;
using GoalFlow.Tests.Isolation;
using Microsoft.Extensions.DependencyInjection;
using static GoalFlow.Tests.Isolation.BaseDeAislamiento;
using static GoalFlow.Tests.Mensajeria.AyudantesDeMensajeria;

namespace GoalFlow.Tests.Mensajeria;

/// <summary>
/// Spec 007 T-26 (R-31): la publicación dirigida a un consumidor. Aditiva a la Spec 004: <c>Publicar</c> y sus pruebas no cambian.
/// </summary>
[Collection(ColeccionDeAislamiento.Nombre)]
public class PublicarParaTests(BaseDeAislamiento db)
{
    private static List<GoalFlow.Domain.Tenancy.TenantId> Leer(CanalDeAvisos canal)
    {
        var pistas = new List<GoalFlow.Domain.Tenancy.TenantId>();
        while (canal.Lector.TryRead(out var pista)) pistas.Add(pista);
        return pistas;
    }

    [Fact]
    public async Task T26_con_dos_consumidores_registrados_escribe_una_sola_fila_para_el_nombrado()
    {
        ConsumidorRegistrado<RanuraA> a = null!;
        ConsumidorRegistrado<RanuraB> b = null!;
        await using var servicios = db.CrearServicios(s =>
        {
            a = s.AddConsumidorDePrueba<RanuraA>();
            b = s.AddConsumidorDePrueba<RanuraB>();
        });
        var agregado = Guid.NewGuid();

        await using (var scope = servicios.ScopeDe(LigaA))
        {
            await using var transaccion = await scope.Iniciar();
            scope.ServiceProvider.GetRequiredService<IPublicadorDeEventos>().PublicarPara(new EventoDePrueba(LigaA, agregado, 1), a.Nombre);
            await scope.Contexto().SaveChangesAsync();
            await transaccion.ConfirmarAsync(CancellationToken.None);
        }

        var filas = await db.LeerEntregas(a.Nombre, b.Nombre);
        var fila = Assert.Single(filas, f => f.AgregadoId == agregado);
        Assert.Equal((a.Nombre, "Pendiente", 0, LigaA.Value, 1), (fila.Consumidor, fila.Estado, fila.Intentos, fila.TenantId, fila.Version));
        Assert.DoesNotContain(filas, f => f.Consumidor == b.Nombre && f.AgregadoId == agregado); // sin fan-out
    }

    [Fact]
    public async Task T26_un_consumidor_desconocido_lanza_ConsumidorNoRegistrado_y_no_deja_nada_en_el_rastreador()
    {
        await using var servicios = db.CrearServicios(s => s.AddConsumidorDePrueba<RanuraA>());
        await using var scope = servicios.ScopeDe(LigaA);
        await using var transaccion = await scope.Iniciar();

        Assert.Throws<ConsumidorNoRegistradoException>(() => scope.ServiceProvider.GetRequiredService<IPublicadorDeEventos>()
            .PublicarPara(new EventoDePrueba(LigaA, Guid.NewGuid(), 1), "no-existe"));

        Assert.Empty(scope.Contexto().ChangeTracker.Entries<EntregaEvento>());
    }

    [Fact]
    public async Task T26_un_consumidor_registrado_para_otro_tipo_de_evento_tampoco_cuenta()
    {
        var nombre = NombreUnico("prueba-de-otro-evento");
        await using var servicios = db.CrearServicios(s => s.AddConsumidorDeEvento<PartidoGuardado, ConsumidorDePartidoA>(nombre));
        await using var scope = servicios.ScopeDe(LigaA);
        await using var transaccion = await scope.Iniciar();

        Assert.Throws<ConsumidorNoRegistradoException>(() => scope.ServiceProvider.GetRequiredService<IPublicadorDeEventos>()
            .PublicarPara(new EventoDePrueba(LigaA, Guid.NewGuid(), 1), nombre));
    }

    [Fact]
    public async Task T26_exige_una_transaccion_activa()
    {
        ConsumidorRegistrado<RanuraA> a = null!;
        await using var servicios = db.CrearServicios(s => a = s.AddConsumidorDePrueba<RanuraA>());
        await using var scope = servicios.ScopeDe(LigaA);

        Assert.Throws<TransaccionRequeridaException>(() => scope.ServiceProvider.GetRequiredService<IPublicadorDeEventos>()
            .PublicarPara(new EventoDePrueba(LigaA, Guid.NewGuid(), 1), a.Nombre));

        Assert.Empty(scope.Contexto().ChangeTracker.Entries<EntregaEvento>());
    }

    [Fact]
    public async Task T26_no_guarda_y_la_pista_sale_solo_tras_el_commit()
    {
        ConsumidorRegistrado<RanuraA> a = null!;
        await using var servicios = db.CrearServicios(s => a = s.AddConsumidorDePrueba<RanuraA>());
        var canal = servicios.GetRequiredService<CanalDeAvisos>();
        var agregado = Guid.NewGuid();

        await using var scope = servicios.ScopeDe(LigaA);
        await using var transaccion = await scope.Iniciar();
        scope.ServiceProvider.GetRequiredService<IPublicadorDeEventos>().PublicarPara(new EventoDePrueba(LigaA, agregado, 1), a.Nombre);

        // PublicarPara no guarda: la fila está solo en el rastreador, no en la base ni en el canal.
        Assert.Single(scope.Contexto().ChangeTracker.Entries<EntregaEvento>());
        Assert.DoesNotContain(await db.LeerEntregas(a.Nombre), f => f.AgregadoId == agregado);
        Assert.Empty(Leer(canal));

        await scope.Contexto().SaveChangesAsync();
        Assert.Empty(Leer(canal)); // tras SaveChanges, antes del commit

        await transaccion.ConfirmarAsync(CancellationToken.None);
        Assert.Equal([LigaA], Leer(canal));
        Assert.Contains(await db.LeerEntregas(a.Nombre), f => f.AgregadoId == agregado);
    }

    [Fact]
    public async Task T26_un_duplicado_sale_como_EntregaDuplicada_con_sus_claves_pendientes()
    {
        ConsumidorRegistrado<RanuraA> a = null!;
        ConsumidorRegistrado<RanuraB> b = null!;
        await using var servicios = db.CrearServicios(s =>
        {
            a = s.AddConsumidorDePrueba<RanuraA>();
            b = s.AddConsumidorDePrueba<RanuraB>();
        });
        var agregado = Guid.NewGuid();
        await using (var primera = servicios.ScopeDe(LigaA))
        {
            await using var transaccion = await primera.Iniciar();
            primera.ServiceProvider.GetRequiredService<IPublicadorDeEventos>().PublicarPara(new EventoDePrueba(LigaA, agregado, 1), a.Nombre);
            await primera.Contexto().SaveChangesAsync();
            await transaccion.ConfirmarAsync(CancellationToken.None);
        }

        await using var scope = servicios.ScopeDe(LigaA);
        await using var segunda = await scope.Iniciar();
        scope.ServiceProvider.GetRequiredService<IPublicadorDeEventos>().PublicarPara(new EventoDePrueba(LigaA, agregado, 1), a.Nombre);

        var duplicada = await Assert.ThrowsAsync<EntregaDuplicadaException>(() => scope.Contexto().SaveChangesAsync());

        var clave = Assert.Single(duplicada.Claves);
        Assert.Equal(new ClaveLogicaDeEntrega(LigaA, EventoDePrueba.NombreDelEvento, agregado, 1, a.Nombre), clave);
        Assert.DoesNotContain(duplicada.Claves, c => c.Consumidor == b.Nombre);
    }

    [Fact]
    public async Task T26_Publicar_sigue_haciendo_fan_out_a_todos_los_consumidores_del_evento()
    {
        ConsumidorRegistrado<RanuraA> a = null!;
        ConsumidorRegistrado<RanuraB> b = null!;
        await using var servicios = db.CrearServicios(s =>
        {
            a = s.AddConsumidorDePrueba<RanuraA>();
            b = s.AddConsumidorDePrueba<RanuraB>();
        });
        var agregado = Guid.NewGuid();

        await ProducirAsync(servicios, LigaA, agregado, 1, null, confirmar: true);

        var filas = await db.LeerEntregas(a.Nombre, b.Nombre);
        Assert.Equal(2, filas.Count(f => f.AgregadoId == agregado));
    }
}
