using System.Diagnostics;
using System.Threading.Channels;
using GoalFlow.Application.Mensajeria;
using GoalFlow.Domain.Tenancy;
using GoalFlow.Infrastructure.Mensajeria;
using GoalFlow.Tests.Isolation;
using Microsoft.Extensions.DependencyInjection;
using static GoalFlow.Tests.Isolation.BaseDeAislamiento;
using static GoalFlow.Tests.Mensajeria.AyudantesDeMensajeria;

namespace GoalFlow.Tests.Mensajeria;

/// <summary>Spec 004 T-04 y T-32: las pistas al ciclo salen del hook de commit y nunca bloquean al productor.</summary>
[Collection(ColeccionDeAislamiento.Nombre)]
public class AvisosTests(BaseDeAislamiento db)
{
    private static List<TenantId> Leer(CanalDeAvisos canal)
    {
        var pistas = new List<TenantId>();
        while (canal.Lector.TryRead(out var pista)) pistas.Add(pista);
        return pistas;
    }

    [Fact]
    public async Task T04_el_canal_real_es_acotado_a_64_con_Wait_y_un_solo_lector()
    {
        await using var servicios = db.CrearServicios();

        var configuracion = servicios.GetRequiredService<CanalDeAvisos>().Configuracion;

        Assert.Equal(64, configuracion.Capacity);
        Assert.Equal(BoundedChannelFullMode.Wait, configuracion.FullMode);
        Assert.True(configuracion.SingleReader);
    }

    [Fact]
    public async Task T04_con_el_canal_lleno_el_commit_no_espera_y_la_entrega_existe()
    {
        ConsumidorRegistrado<RanuraA> a = null!;
        await using var servicios = db.CrearServicios(s =>
        {
            a = s.AddConsumidorDePrueba<RanuraA>();
            s.Configure<OpcionesDeMensajeria>(o => o.CapacidadDelCanal = 1);
        });
        var canal = servicios.GetRequiredService<CanalDeAvisos>();

        // Calienta el pool para que el tiempo medido sea el del commit y no el de abrir la primera conexión.
        await ProducirAsync(servicios, LigaA, Guid.NewGuid(), 1, null, confirmar: true);
        Leer(canal);

        Assert.True(canal.IntentarAvisar(LigaB)); // Ahora el canal está lleno.
        var agregado = Guid.NewGuid();
        var reloj = Stopwatch.StartNew();
        var produccion = ProducirAsync(servicios, LigaA, agregado, 1, null, confirmar: true);
        var terminada = await Task.WhenAny(produccion, Task.Delay(TimeSpan.FromSeconds(1)));
        reloj.Stop();

        Assert.Same(produccion, terminada);
        await produccion;
        Assert.Contains(await db.LeerEntregas(a.Nombre), f => f.AgregadoId == agregado);
        Assert.Equal([LigaB], Leer(canal)); // Solo la pista vieja: la nueva se perdió y el temporizador la recupera.
    }

    [Fact]
    public async Task T32_la_pista_sale_tras_el_commit_y_no_antes()
    {
        await using var servicios = db.CrearServicios(s => s.AddConsumidorDePrueba<RanuraA>());
        var canal = servicios.GetRequiredService<CanalDeAvisos>();

        await using var scope = servicios.ScopeDe(LigaA);
        await using var transaccion = await scope.Iniciar();
        scope.ServiceProvider.GetRequiredService<IPublicadorDeEventos>().Publicar(new EventoDePrueba(LigaA, Guid.NewGuid(), 1));
        Assert.Empty(Leer(canal)); // Tras publicar.

        await scope.Contexto().SaveChangesAsync();
        Assert.Empty(Leer(canal)); // Tras SaveChanges, antes del commit.

        await transaccion.ConfirmarAsync(CancellationToken.None); // El de UnidadDeTrabajo: el hook corre con él.
        Assert.Equal([LigaA], Leer(canal));
    }

    [Fact]
    public async Task T32_una_transaccion_revertida_no_envia_pistas()
    {
        await using var servicios = db.CrearServicios(s => s.AddConsumidorDePrueba<RanuraA>());
        var canal = servicios.GetRequiredService<CanalDeAvisos>();

        await using var scope = servicios.ScopeDe(LigaA);
        await using var transaccion = await scope.Iniciar();
        scope.ServiceProvider.GetRequiredService<IPublicadorDeEventos>().Publicar(new EventoDePrueba(LigaA, Guid.NewGuid(), 1));
        await scope.Contexto().SaveChangesAsync();

        await scope.Contexto().Database.CurrentTransaction!.RollbackAsync();

        Assert.Empty(Leer(canal));
    }

    [Fact]
    public async Task T32_liberar_sin_confirmar_no_envia_pistas_ni_las_hereda_la_transaccion_siguiente()
    {
        await using var servicios = db.CrearServicios(s => s.AddConsumidorDePrueba<RanuraA>());
        var canal = servicios.GetRequiredService<CanalDeAvisos>();

        await using var scope = servicios.ScopeDe(LigaA);
        await using (var transaccion = await scope.Iniciar())
        {
            scope.ServiceProvider.GetRequiredService<IPublicadorDeEventos>().Publicar(new EventoDePrueba(LigaA, Guid.NewGuid(), 1));
            await scope.Contexto().SaveChangesAsync();
        } // Liberada sin confirmar.

        Assert.Empty(Leer(canal));

        // Mismo scope, transacción nueva que confirma sin publicar nada: no debe salir la pista de la anterior.
        await using (var siguiente = await scope.Iniciar())
            await siguiente.ConfirmarAsync(CancellationToken.None);

        Assert.Empty(Leer(canal));
    }
}
