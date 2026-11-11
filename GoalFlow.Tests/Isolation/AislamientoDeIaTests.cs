using GoalFlow.Application.IA;
using GoalFlow.Application.Persistencia;
using GoalFlow.Application.Tenancy;
using GoalFlow.Domain.Tenancy;
using GoalFlow.Infrastructure.IA;
using GoalFlow.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static GoalFlow.Tests.Isolation.BaseDeAislamiento;

namespace GoalFlow.Tests.Isolation;

/// <summary>
/// Spec 007 T-24 (R-04, R-26): aislamiento de las tres tablas de IA conectado como goalflow_app, por el registro real de DI,
/// y la exigencia de transacción en el contador. Datos inventados.
/// </summary>
[Collection(ColeccionDeAislamiento.Nombre)]
public class AislamientoDeIaTests(BaseDeAislamiento db)
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private ServiceProvider Servicios() => db.CrearServicios(s => s.AddGoalFlowIA());

    private static AsyncServiceScope ScopeDe(ServiceProvider servicios, TenantId tenant)
    {
        var scope = servicios.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().Establecer(tenant);
        return scope;
    }

    private static GoalFlowDbContext Contexto(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<GoalFlowDbContext>();

    private static Task<ITransaccion> Iniciar(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IUnidadDeTrabajo>().IniciarAsync(Ct);

    private async Task<(long Reservado, long Consumido)> LeerContadorComoSuperusuario(Guid id)
    {
        await using var conexion = new NpgsqlConnection(db.CadenaSuperusuario);
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand("select reservado, consumido from goalflow.presupuestos_ia where id = @id", conexion);
        comando.Parameters.AddWithValue("id", id);
        await using var lector = await comando.ExecuteReaderAsync();
        await lector.ReadAsync();
        return (lector.GetInt64(0), lector.GetInt64(1));
    }

    [Fact]
    public async Task T24_un_scope_de_A_solo_lee_sus_contadores_llamadas_y_resumenes()
    {
        var g = await GrafoDeIa.ObtenerAsync(db);
        await using var servicios = Servicios();
        await using var scope = ScopeDe(servicios, LigaA);
        await using var transaccion = await Iniciar(scope);
        var contexto = Contexto(scope);

        var contadores = await contexto.PresupuestosIa.AsNoTracking().ToListAsync(Ct);
        var llamadas = await contexto.LlamadasIa.AsNoTracking().ToListAsync(Ct);
        var resumenes = await contexto.ResumenesPartido.AsNoTracking().ToListAsync(Ct);

        Assert.Contains(contadores, c => c.Id == g.ContadorA);
        Assert.Contains(llamadas, l => l.Id == g.LlamadaA);
        Assert.Contains(resumenes, r => r.Id == g.ResumenA);
        Assert.All(contadores, c => Assert.Equal(LigaA, c.TenantId));
        Assert.All(llamadas, l => Assert.Equal(LigaA, l.TenantId));
        Assert.All(resumenes, r => Assert.Equal(LigaA, r.TenantId));
        Assert.DoesNotContain(contadores, c => c.Id == g.ContadorB);
        Assert.DoesNotContain(llamadas, l => l.Id == g.LlamadaB);
        Assert.DoesNotContain(resumenes, r => r.Id == g.ResumenB);
    }

    [Theory]
    [InlineData("presupuestos_ia")]
    [InlineData("llamadas_ia")]
    [InlineData("resumenes_partido")]
    public async Task T24_la_politica_RLS_deja_cero_filas_ajenas_y_cero_filas_sin_tenant(string tabla)
    {
        await GrafoDeIa.ObtenerAsync(db);
        await using var conexion = new NpgsqlConnection(db.CadenaApp);
        await conexion.OpenAsync();

        async Task<(long Propias, long Ajenas)> Contar(Guid? tenant)
        {
            await using var transaccion = await conexion.BeginTransactionAsync();
            if (tenant is { } t)
            {
                await using var fijar = new NpgsqlCommand($"select set_config('app.tenant_id', '{t}', true)", conexion, transaccion);
                await fijar.ExecuteNonQueryAsync();
            }

            var referencia = tenant ?? Guid.Empty;
            await using var comando = new NpgsqlCommand($"""
                select count(*) filter (where tenant_id = '{referencia}'), count(*) filter (where tenant_id <> '{referencia}')
                from goalflow.{tabla}
                """, conexion, transaccion);
            await using var lector = await comando.ExecuteReaderAsync();
            await lector.ReadAsync();
            var resultado = (lector.GetInt64(0), lector.GetInt64(1));
            await lector.CloseAsync();
            await transaccion.RollbackAsync();
            return resultado;
        }

        var (propiasA, ajenasA) = await Contar(LigaA.Value);
        var (propiasB, ajenasB) = await Contar(LigaB.Value);
        var sinTenant = await Contar(null);

        Assert.True(propiasA >= 1 && propiasB >= 1);
        Assert.Equal(0, ajenasA);
        Assert.Equal(0, ajenasB);
        Assert.Equal((0, 0), sinTenant);
    }

    [Fact]
    public async Task T24_un_scope_de_A_no_actualiza_el_contador_de_B()
    {
        var g = await GrafoDeIa.ObtenerAsync(db);
        await using var servicios = Servicios();
        await using var scope = ScopeDe(servicios, LigaA);
        await using var transaccion = await Iniciar(scope);

        var afectadas = await Contexto(scope).PresupuestosIa
            .Where(c => c.Id == g.ContadorB)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Reservado, 999), Ct);

        Assert.Equal(0, afectadas);
        Assert.Equal((0, 0), await LeerContadorComoSuperusuario(g.ContadorB));
    }

    [Fact]
    public async Task T24_la_reserva_de_A_solo_afecta_el_contador_de_A()
    {
        var g = await GrafoDeIa.ObtenerAsync(db);
        await using var servicios = Servicios();
        await using var scope = ScopeDe(servicios, LigaA);
        await using var transaccion = await Iniciar(scope); // sin confirmar: se revierte
        var almacen = scope.ServiceProvider.GetRequiredService<IAlmacenDePresupuesto>();

        var reservada = await almacen.ReservarAsync(GrafoDeIa.MesFijo, 100, Ct);
        var visto = await Contexto(scope).PresupuestosIa.AsNoTracking().SingleAsync(c => c.Mes == GrafoDeIa.MesFijo, Ct);

        Assert.True(reservada);
        Assert.Equal(LigaA, visto.TenantId);
        Assert.Equal(g.ContadorA, visto.Id);
        Assert.Equal(100, visto.Reservado);
        Assert.Equal((0, 0), await LeerContadorComoSuperusuario(g.ContadorB));
    }

    [Fact]
    public async Task T24_ExecuteUpdate_del_contador_sin_transaccion_lanza_TransaccionRequerida_y_no_es_cero_filas()
    {
        await GrafoDeIa.ObtenerAsync(db);
        await using var servicios = Servicios();
        await using var scope = ScopeDe(servicios, LigaA);
        var almacen = scope.ServiceProvider.GetRequiredService<IAlmacenDePresupuesto>();

        await Assert.ThrowsAsync<TransaccionRequeridaException>(() => almacen.ReservarAsync(GrafoDeIa.MesFijo, 100, Ct));

        // Con transacción, un mes sin contador devuelve false (cero filas): es otra cosa que lanzar.
        await using var transaccion = await Iniciar(scope);
        Assert.False(await almacen.ReservarAsync("2099-12", 100, Ct));
    }

    [Fact]
    public async Task T24_la_reserva_que_excede_el_tope_devuelve_false_y_no_cambia_el_contador()
    {
        var g = await GrafoDeIa.ObtenerAsync(db);
        await using var servicios = Servicios();
        await using var scope = ScopeDe(servicios, LigaA);
        await using var transaccion = await Iniciar(scope);
        var almacen = scope.ServiceProvider.GetRequiredService<IAlmacenDePresupuesto>();

        Assert.False(await almacen.ReservarAsync(GrafoDeIa.MesFijo, 1_500_001, Ct));
        Assert.True(await almacen.ReservarAsync(GrafoDeIa.MesFijo, 1_500_000, Ct));
        Assert.False(await almacen.ReservarAsync(GrafoDeIa.MesFijo, 1, Ct));
        Assert.Equal((0, 0), await LeerContadorComoSuperusuario(g.ContadorA)); // nada confirmado
    }
}
