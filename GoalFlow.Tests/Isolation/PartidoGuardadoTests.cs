using GoalFlow.Application.Mensajeria;
using GoalFlow.Application.Torneos.Contracts;
using GoalFlow.Domain.Tenancy;
using GoalFlow.Infrastructure.Mensajeria;
using GoalFlow.Infrastructure.Persistencia;
using GoalFlow.Infrastructure.Torneos;
using GoalFlow.Tests.Mensajeria;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using static GoalFlow.Tests.Isolation.BaseDeAislamiento;
using static GoalFlow.Tests.Isolation.EntornoDePlanilla;
using static GoalFlow.Tests.Mensajeria.AyudantesDeMensajeria;

namespace GoalFlow.Tests.Isolation;

/// <summary>Consumidores de prueba de <c>PartidoGuardado</c>: viven solo aquí, nunca en producción (Spec 004 §4).</summary>
public sealed class ConsumidorDePartidoA : IConsumidorDeEvento<PartidoGuardado>
{
    public static string Nombre => "prueba-partido-a";

    public Task ProcesarAsync(EntregaDeEvento<PartidoGuardado> entrega, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class ConsumidorDePartidoB : IConsumidorDeEvento<PartidoGuardado>
{
    public static string Nombre => "prueba-partido-b";

    public Task ProcesarAsync(EntregaDeEvento<PartidoGuardado> entrega, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>Spec 006 T-16 (R-23, R-24) y T-26 (R-20, C-30): el evento y la traducción de los duplicados de entrega.</summary>
[Collection(ColeccionDePlanilla.Nombre)]
public class PartidoGuardadoTests(BaseDeAislamiento db)
{
    private const string Evento = "torneos.partido-guardado";

    private static string NombreUnico(string basico) => $"{basico}-{Guid.NewGuid():N}";

    private async Task<int[]> VersionesEntregadas(EntornoDePlanilla e, string p, string consumidor)
    {
        var agregado = Guid.Parse((await e.Texto($"select ({e.FiltroDePartido(p, 1, 2)})::text"))!);
        var filas = await db.LeerEntregas(consumidor);
        Assert.All(filas, f => Assert.Equal(LigaA.Value, f.TenantId));
        return [.. filas.Where(f => f.AgregadoId == agregado).Select(f => f.Version).Order()];
    }

    // ---- T-16 ----

    [Fact]
    public async Task T16_cada_version_nueva_publica_una_entrega_y_los_guardados_identicos_o_rechazados_ninguna()
    {
        var consumidor = NombreUnico("prueba-partido");
        await using var e = new EntornoDePlanilla(db, ajustarAnfitrion: b =>
            b.ConfigureTestServices(s => s.AddConsumidorDeEvento<PartidoGuardado, ConsumidorDePartidoA>(consumidor)));
        var p = await e.Preparar();
        var ruta = RutaDelPrimerPartido(p);

        var primera = await e.Guardar(ruta, Jugado3a1(p, 0));
        Assert.Equal(System.Net.HttpStatusCode.OK, primera.StatusCode);
        Assert.Equal([1], await VersionesEntregadas(e, p, consumidor));

        // Un guardado idéntico no agrega nada.
        await e.Guardar(ruta, Jugado3a1(p, 1));
        Assert.Equal([1], await VersionesEntregadas(e, p, consumidor));

        // Una corrección agrega una entrega con la versión 2.
        await e.Guardar(ruta, Corregido3a1(p, 1));
        Assert.Equal([1, 2], await VersionesEntregadas(e, p, consumidor));

        // Un guardado desfasado (409) no agrega nada.
        var vieja = await e.Guardar(ruta, Jugado3a1(p, 1) with { Marcador = new(4, 1), Goles = [new(Jugador(p, 1, 1), 3), new(Jugador(p, 2, 2), 1)] });
        Assert.Equal(System.Net.HttpStatusCode.Conflict, vieja.StatusCode);
        Assert.Equal([1, 2], await VersionesEntregadas(e, p, consumidor));

        // Un guardado inválido (422) tampoco.
        await e.Guardar(ruta, Jugado3a1(p, 2) with { Oficial = new(1, 0) });
        Assert.Equal([1, 2], await VersionesEntregadas(e, p, consumidor));

        var fila = Assert.Single((await db.LeerEntregas(consumidor)).Where(f => f.Version == 2));
        Assert.Equal("Pendiente", fila.Estado);
    }

    [Fact]
    public async Task T16_sin_consumidor_registrado_el_guardado_funciona_y_no_escribe_ninguna_entrega()
    {
        await using var e = new EntornoDePlanilla(db);
        var p = await e.Preparar();
        var ruta = RutaDelPrimerPartido(p);

        var respuesta = await e.Guardar(ruta, Jugado3a1(p, 0));

        Assert.Equal(System.Net.HttpStatusCode.OK, respuesta.StatusCode);
        var entregas = await e.Contar($"""
            select count(*) from goalflow.entregas_eventos
            where tipo_de_evento = '{Evento}' and agregado_id = ({e.FiltroDePartido(p, 1, 2)})
            """);
        Assert.Equal(0, entregas);
    }

    [Fact]
    public void T16_el_nombre_estable_del_evento_es_el_de_la_Spec()
    {
        Assert.Equal(Evento, PartidoGuardado.NombreDelEvento);
    }

    // ---- T-26: el predicado de traducción (unidad) ----

    private static readonly TenantId Tenant = new(Guid.NewGuid());
    private static readonly Guid Partido = Guid.NewGuid();

    private static EntregaDuplicadaException Duplicada(params ClaveLogicaDeEntrega[] claves) => new(new Exception("original"), claves);

    private static ClaveLogicaDeEntrega Clave(
        TenantId? tenant = null, string evento = Evento, Guid? agregado = null, int version = 3, string consumidor = "c1") =>
        new(tenant ?? Tenant, evento, agregado ?? Partido, version, consumidor);

    [Fact]
    public void T26_todas_las_claves_de_este_partido_y_esta_version_son_un_conflicto_de_version()
    {
        var duplicada = Duplicada(Clave(consumidor: "c1"), Clave(consumidor: "c2"));

        Assert.True(AlmacenDePlanilla.EsConflictoDeVersion(duplicada, Tenant, Partido, 3));
    }

    [Fact]
    public void T26_una_clave_de_otra_version_otro_agregado_otro_evento_u_otro_tenant_no_lo_es()
    {
        Assert.False(AlmacenDePlanilla.EsConflictoDeVersion(Duplicada(Clave(), Clave(version: 4, consumidor: "c2")), Tenant, Partido, 3));
        Assert.False(AlmacenDePlanilla.EsConflictoDeVersion(Duplicada(Clave(), Clave(agregado: Guid.NewGuid(), consumidor: "c2")), Tenant, Partido, 3));
        Assert.False(AlmacenDePlanilla.EsConflictoDeVersion(Duplicada(Clave(), Clave(evento: "prueba.evento", consumidor: "c2")), Tenant, Partido, 3));
        Assert.False(AlmacenDePlanilla.EsConflictoDeVersion(Duplicada(Clave(), Clave(tenant: new TenantId(Guid.NewGuid()), consumidor: "c2")), Tenant, Partido, 3));
    }

    [Fact]
    public void T26_una_excepcion_sin_claves_no_es_un_conflicto_de_version()
    {
        Assert.False(AlmacenDePlanilla.EsConflictoDeVersion(Duplicada(), Tenant, Partido, 3));
        Assert.False(AlmacenDePlanilla.EsConflictoDeVersion(new EntregaDuplicadaException(new Exception("original")), Tenant, Partido, 3));
    }

    // ---- T-26: la excepción expone las claves de las filas que intentó insertar (integración) ----

    [Fact]
    public async Task T26_la_excepcion_de_entrega_duplicada_expone_exactamente_las_claves_de_las_filas_que_intento_insertar()
    {
        var (a, b) = (NombreUnico("prueba-a"), NombreUnico("prueba-b"));
        await using var servicios = db.CrearServicios(s =>
        {
            s.AddConsumidorDeEvento<PartidoGuardado, ConsumidorDePartidoA>(a);
            s.AddConsumidorDeEvento<PartidoGuardado, ConsumidorDePartidoB>(b);
        });
        var agregado = Guid.NewGuid();

        await using var scope = servicios.ScopeDe(LigaA);
        await using var transaccion = await scope.Iniciar();
        var publicador = scope.ServiceProvider.GetRequiredService<GoalFlow.Application.Mensajeria.IPublicadorDeEventos>();
        publicador.Publicar(new PartidoGuardado(LigaA, agregado, 1));
        await scope.Contexto().SaveChangesAsync(); // la primera publicación se guarda

        publicador.Publicar(new PartidoGuardado(LigaA, agregado, 1)); // la misma clave lógica otra vez
        var duplicada = await Assert.ThrowsAsync<EntregaDuplicadaException>(() => scope.Contexto().SaveChangesAsync());

        Assert.Equal(
            new[]
            {
                new ClaveLogicaDeEntrega(LigaA, Evento, agregado, 1, a),
                new ClaveLogicaDeEntrega(LigaA, Evento, agregado, 1, b),
            }.OrderBy(c => c.Consumidor, StringComparer.Ordinal).ToArray(),
            duplicada.Claves.OrderBy(c => c.Consumidor, StringComparer.Ordinal).ToArray());
    }
}
