using System.Data.Common;
using GoalFlow.Application.Ligas;
using GoalFlow.Application.Persistencia;
using GoalFlow.Application.Tenancy;
using GoalFlow.Domain.Ligas;
using GoalFlow.Domain.Tenancy;
using GoalFlow.Domain.Torneos;
using GoalFlow.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using static GoalFlow.Tests.Isolation.BaseDeAislamiento;

namespace GoalFlow.Tests.Isolation;

/// <summary>Capa de aplicación: el registro real de DI, conectado como goalflow_app.</summary>
[Collection(ColeccionDeAislamiento.Nombre)]
public class AislamientoEfCoreTests(BaseDeAislamiento db)
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static GoalFlowDbContext Contexto(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<GoalFlowDbContext>();

    private static Task<ITransaccion> Iniciar(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IUnidadDeTrabajo>().IniciarAsync(Ct);

    private static AsyncServiceScope ScopeDe(ServiceProvider servicios, TenantId? tenant = null)
    {
        var scope = servicios.CreateAsyncScope();
        if (tenant is { } valor) scope.ServiceProvider.GetRequiredService<ITenantContext>().Establecer(valor);
        return scope;
    }

    private static async Task<List<Equipo>> LeerEquipos(ServiceProvider servicios, TenantId tenant)
    {
        await using var scope = ScopeDe(servicios, tenant);
        await using var transaccion = await Iniciar(scope);
        var equipos = await Contexto(scope).Equipos.AsNoTracking().ToListAsync();
        await transaccion.ConfirmarAsync(Ct);
        return equipos;
    }

    private static string NombreUnico() => $"equipo-{Guid.NewGuid():N}";

    [Fact]
    public async Task T05_dos_scopes_consecutivos_ven_cada_uno_su_liga()
    {
        await using var servicios = db.CrearServicios();

        var deA = await LeerEquipos(servicios, LigaA);
        var deB = await LeerEquipos(servicios, LigaB);

        Assert.Contains(deA, e => e.Nombre == EquipoDeA);
        Assert.All(deA, e => Assert.Equal(LigaA, e.TenantId));
        Assert.Contains(deB, e => e.Nombre == EquipoDeB);
        Assert.All(deB, e => Assert.Equal(LigaB, e.TenantId));
    }

    [Fact]
    public async Task T06_agregar_con_tenant_vacio_asigna_el_tenant_actual()
    {
        await using var servicios = db.CrearServicios();
        var nombre = NombreUnico();

        await using (var scope = ScopeDe(servicios, LigaA))
        {
            await using var transaccion = await Iniciar(scope);
            Contexto(scope).Equipos.Add(new Equipo(Guid.NewGuid(), default, nombre));
            await Contexto(scope).SaveChangesAsync();
            await transaccion.ConfirmarAsync(Ct);
        }

        Assert.Equal(LigaA, Assert.Single(await LeerEquipos(servicios, LigaA), e => e.Nombre == nombre).TenantId);
    }

    [Fact]
    public async Task T07_agregar_con_tenant_ajeno_lanza_y_no_persiste()
    {
        await using var servicios = db.CrearServicios();
        var nombre = NombreUnico();

        await using (var scope = ScopeDe(servicios, LigaA))
        {
            await using var transaccion = await Iniciar(scope);
            Contexto(scope).Equipos.Add(new Equipo(Guid.NewGuid(), LigaB, nombre));

            await Assert.ThrowsAsync<TenantInvalidoException>(() => Contexto(scope).SaveChangesAsync());
        }

        Assert.Equal(0, await db.ContarComoSuperusuario(nombre));
    }

    [Fact]
    public async Task T07_cambiar_el_tenant_de_una_entidad_rastreada_lanza()
    {
        await using var servicios = db.CrearServicios();
        await using var scope = ScopeDe(servicios, LigaA);
        await using var transaccion = await Iniciar(scope);
        var contexto = Contexto(scope);
        var equipo = await contexto.Equipos.FirstAsync(e => e.Nombre == EquipoDeA);

        contexto.Entry(equipo).Property(e => e.TenantId).CurrentValue = LigaB;

        await Assert.ThrowsAsync<TenantInvalidoException>(() => contexto.SaveChangesAsync());
    }

    [Fact]
    public async Task T08_SaveChanges_sin_transaccion_lanza_y_no_persiste()
    {
        await using var servicios = db.CrearServicios();
        var nombre = NombreUnico();
        await using var scope = ScopeDe(servicios, LigaA);

        Contexto(scope).Equipos.Add(new Equipo(Guid.NewGuid(), LigaA, nombre));

        await Assert.ThrowsAsync<TransaccionRequeridaException>(() => Contexto(scope).SaveChangesAsync());
        Assert.Equal(0, await db.ContarComoSuperusuario(nombre));
    }

    [Fact]
    public async Task T17_lo_guardado_por_el_puerto_se_lee_en_otra_transaccion()
    {
        await using var servicios = db.CrearServicios();
        var nombre = NombreUnico();

        await using (var scope = ScopeDe(servicios, LigaA))
        {
            await using var transaccion = await Iniciar(scope);
            Contexto(scope).Equipos.Add(new Equipo(Guid.NewGuid(), LigaA, nombre));
            await Contexto(scope).SaveChangesAsync();
            await transaccion.ConfirmarAsync(Ct);
        }

        Assert.Contains(await LeerEquipos(servicios, LigaA), e => e.Nombre == nombre);
    }

    private sealed class ContadorDeConexiones : DbConnectionInterceptor
    {
        public int Aperturas;

        public override InterceptionResult ConnectionOpening(
            DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
        {
            Aperturas++;
            return result;
        }

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            Aperturas++;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class ContadorDeComandos : DbCommandInterceptor
    {
        public int Ejecutados;

        public override DbCommand CommandInitialized(CommandEndEventData eventData, DbCommand result)
        {
            Ejecutados++;
            return result;
        }
    }

    [Fact]
    public async Task T18_por_el_puerto_sin_contexto_lanza_sin_abrir_la_conexion()
    {
        var conexiones = new ContadorDeConexiones();
        await using var servicios = db.CrearServicios(s =>
            s.ConfigureDbContext<GoalFlowDbContext>(o => o.AddInterceptors(conexiones)));
        await using var scope = ScopeDe(servicios);

        await Assert.ThrowsAsync<ContextoDeTransaccionAusenteException>(() => Iniciar(scope));

        Assert.Equal(0, conexiones.Aperturas);
    }

    [Fact]
    public async Task T18_BeginTransaction_directo_sin_contexto_no_crea_transaccion_ni_ejecuta_comandos()
    {
        var comandos = new ContadorDeComandos();
        var transaccionesIniciadas = 0;
        await using var servicios = db.CrearServicios(s => s.ConfigureDbContext<GoalFlowDbContext>(o => o
            .AddInterceptors(comandos)
            // CurrentTransaction no basta: EF Core la asigna después del hook TransactionStarted, así que
            // lanzar ahí también la deja en null. El evento se emite antes del hook y delata esa transacción.
            .LogTo(_ => transaccionesIniciadas++, [RelationalEventId.TransactionStarted])));

        await using (var scope = ScopeDe(servicios))
        {
            var contexto = Contexto(scope);

            await Assert.ThrowsAsync<ContextoDeTransaccionAusenteException>(
                () => contexto.Database.BeginTransactionAsync());

            Assert.Null(contexto.Database.CurrentTransaction);
            Assert.Equal(0, comandos.Ejecutados);
            Assert.Equal(0, transaccionesIniciadas);
        }

        // Control positivo: con contexto, el mismo contador sí registra la transacción.
        await using (var scope = ScopeDe(servicios, LigaA))
        {
            await using var transaccion = await Contexto(scope).Database.BeginTransactionAsync();
            Assert.Equal(1, transaccionesIniciadas);
        }
    }

    [Fact]
    public async Task T19_con_identidad_sin_tenant_lee_sus_admins_y_Equipo_lanza()
    {
        await using var servicios = db.CrearServicios();
        await using var scope = ScopeDe(servicios);
        scope.ServiceProvider.GetRequiredService<IIdentidadAutenticada>().Establecer(UsuarioU1);
        await using var transaccion = await Iniciar(scope);

        List<AdminLiga> admins = await Contexto(scope).AdminsLiga.AsNoTracking().ToListAsync();

        Assert.Equal(UsuarioU1, Assert.Single(admins).UsuarioId);
        await Assert.ThrowsAsync<TenantAusenteException>(() => Contexto(scope).Equipos.ToListAsync());
    }

    [Fact]
    public async Task T20_el_resolutor_funciona_sin_tenant_ni_identidad()
    {
        await using var servicios = db.CrearServicios();
        await using var scope = ScopeDe(servicios);
        var resolutor = scope.ServiceProvider.GetRequiredService<IResolutorDeSlug>();

        var liga = await resolutor.ResolverAsync("liga-a", Ct);

        Assert.Equal(LigaA, liga?.Id);
        Assert.Null(await resolutor.ResolverAsync("no-existe", Ct));
    }

    [Fact]
    public async Task T21_con_tenant_sin_transaccion_no_hay_filas_y_con_transaccion_si()
    {
        await using var servicios = db.CrearServicios();
        await using var scope = ScopeDe(servicios, LigaA);
        var contexto = Contexto(scope);

        Assert.Empty(await contexto.Equipos.AsNoTracking().ToListAsync());

        await using var transaccion = await Iniciar(scope);
        Assert.Contains(await contexto.Equipos.AsNoTracking().ToListAsync(), e => e.Nombre == EquipoDeA);
    }
}
