using GoalFlow.Application.Mensajeria;
using GoalFlow.Application.Persistencia;
using GoalFlow.Domain.Tenancy;
using GoalFlow.Infrastructure.Mensajeria;
using GoalFlow.Infrastructure.Persistencia;
using GoalFlow.Tests.Isolation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using static GoalFlow.Tests.Isolation.BaseDeAislamiento;
using static GoalFlow.Tests.Mensajeria.AyudantesDeMensajeria;

namespace GoalFlow.Tests.Mensajeria;

/// <summary>Spec 004: publicación, tabla de entregas y fuente de tenants (T-01, T-02, T-12, T-13, T-20, T-22, T-30, T-31).</summary>
[Collection(ColeccionDeAislamiento.Nombre)]
public class PublicacionTests(BaseDeAislamiento db)
{
    [Fact]
    public async Task T01_la_publicacion_se_revierte_con_el_productor_y_hace_fan_out_al_confirmar()
    {
        ConsumidorRegistrado<RanuraA> a = null!;
        ConsumidorRegistrado<RanuraB> b = null!;
        await using var servicios = db.CrearServicios(s =>
        {
            a = s.AddConsumidorDePrueba<RanuraA>();
            b = s.AddConsumidorDePrueba<RanuraB>();
        });
        var equipo = NombreUnico("equipo");

        await ProducirAsync(servicios, LigaA, Guid.NewGuid(), 1, equipo, confirmar: false);

        Assert.Equal(0, await db.ContarComoSuperusuario(equipo));
        Assert.Empty(await db.LeerEntregas(a.Nombre, b.Nombre));

        await ProducirAsync(servicios, LigaA, Guid.NewGuid(), 1, equipo, confirmar: true);

        Assert.Equal(1, await db.ContarComoSuperusuario(equipo));
        var filas = await db.LeerEntregas(a.Nombre, b.Nombre);
        Assert.Equal(2, filas.Count);
        Assert.All(filas, f =>
        {
            Assert.Equal("Pendiente", f.Estado);
            Assert.Equal(0, f.Intentos);
            Assert.Equal(LigaA.Value, f.TenantId);
        });
        Assert.Equal([a.Nombre, b.Nombre], filas.Select(f => f.Consumidor).Order());
    }

    [Fact]
    public async Task T02_publicar_sin_transaccion_lanza_y_no_deja_nada_en_el_rastreador()
    {
        await using var servicios = db.CrearServicios(s => s.AddConsumidorDePrueba<RanuraA>());
        await using var scope = servicios.ScopeDe(LigaA);

        Assert.Throws<TransaccionRequeridaException>(() =>
            scope.ServiceProvider.GetRequiredService<IPublicadorDeEventos>().Publicar(new EventoDePrueba(LigaA, Guid.NewGuid(), 1)));

        Assert.Empty(scope.Contexto().ChangeTracker.Entries<EntregaEvento>());
    }

    [Fact]
    public async Task T12_la_clave_logica_repetida_lanza_la_excepcion_de_Mensajeria_y_la_transaccion_sigue_usable()
    {
        ConsumidorRegistrado<RanuraA> a = null!;
        await using var servicios = db.CrearServicios(s => a = s.AddConsumidorDePrueba<RanuraA>());
        var agregado = Guid.NewGuid();
        await ProducirAsync(servicios, LigaA, agregado, 1, null, confirmar: true);

        await using (var scope = servicios.ScopeDe(LigaA))
        {
            await using var transaccion = await scope.Iniciar();
            var contexto = scope.Contexto();
            scope.ServiceProvider.GetRequiredService<IPublicadorDeEventos>().Publicar(new EventoDePrueba(LigaA, agregado, 1));

            var duplicada = await Assert.ThrowsAsync<EntregaDuplicadaException>(() => contexto.SaveChangesAsync());

            var original = Assert.IsType<DbUpdateException>(duplicada.InnerException);
            var postgres = Assert.IsType<PostgresException>(original.InnerException);
            Assert.Equal(PostgresErrorCodes.UniqueViolation, postgres.SqlState);

            // Savepoint de EF Core: la transacción no quedó abortada y una consulta posterior funciona.
            Assert.NotEmpty(await contexto.Equipos.AsNoTracking().ToListAsync());
            // Las filas fallidas siguen Added en el rastreador (ADR-02 hecho 10).
            Assert.Single(contexto.ChangeTracker.Entries<EntregaEvento>(), e => e.State == EntityState.Added);
        }

        Assert.Single(await db.LeerEntregas(a.Nombre));
    }

    [Fact]
    public async Task T12_otra_violacion_de_la_base_no_se_traduce()
    {
        await using var servicios = db.CrearServicios(s => s.AddConsumidorDePrueba<RanuraA>());
        var tenantQueNoEsLiga = new TenantId(Guid.NewGuid());

        await using var scope = servicios.ScopeDe(tenantQueNoEsLiga);
        await using var transaccion = await scope.Iniciar();
        scope.ServiceProvider.GetRequiredService<IPublicadorDeEventos>()
            .Publicar(new EventoDePrueba(tenantQueNoEsLiga, Guid.NewGuid(), 1));

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => scope.Contexto().SaveChangesAsync());

        Assert.IsNotType<EntregaDuplicadaException>(error);
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }

    /// <summary>Un llamador idempotente de prueba (ADR-02 §2.3): un agregado por scope y por transacción.</summary>
    private sealed class LlamadaIdempotente(IServiceProvider servicios, TenantId tenant, Guid agregado) : IAsyncDisposable
    {
        private AsyncServiceScope _scope;
        private ITransaccion? _transaccion;

        public bool YaExistia { get; private set; }

        public async Task PrepararAsync()
        {
            _scope = servicios.ScopeDe(tenant);
            _transaccion = await _scope.Iniciar();
            var existe = await _scope.Contexto().EntregasEventos.AnyAsync(e => e.AgregadoId == agregado && e.VersionDelAgregado == 1);
            if (existe) YaExistia = true;
            else _scope.ServiceProvider.GetRequiredService<IPublicadorDeEventos>().Publicar(new EventoDePrueba(tenant, agregado, 1));
        }

        public async Task GuardarAsync()
        {
            if (YaExistia) return;
            try
            {
                await _scope.Contexto().SaveChangesAsync();
            }
            catch (EntregaDuplicadaException)
            {
                YaExistia = true; // Contado como hecho; este scope se descarta entero.
            }
        }

        public async Task ConfirmarAsync()
        {
            if (!YaExistia) await _transaccion!.ConfirmarAsync(CancellationToken.None);
        }

        public async ValueTask DisposeAsync()
        {
            if (_transaccion is not null) await _transaccion.DisposeAsync();
            await _scope.DisposeAsync();
        }
    }


    [Fact]
    public async Task T13_un_llamador_idempotente_concurrente_deja_una_sola_fila_y_ninguno_falla()
    {
        ConsumidorRegistrado<RanuraA> a = null!;
        await using var servicios = db.CrearServicios(s => a = s.AddConsumidorDePrueba<RanuraA>());
        var agregado = Guid.NewGuid();

        await using var primera = new LlamadaIdempotente(servicios, LigaA, agregado);
        await using var segunda = new LlamadaIdempotente(servicios, LigaA, agregado);

        // Orden controlado (Spec 004 C-04): ambas ven "ausente"; la primera guarda sin confirmar;
        // la segunda queda bloqueada en el índice único; la primera confirma; la segunda recibe la excepción.
        await primera.PrepararAsync();
        await segunda.PrepararAsync();
        Assert.False(primera.YaExistia);
        Assert.False(segunda.YaExistia);

        await primera.GuardarAsync();
        var guardadoDeLaSegunda = Task.Run(segunda.GuardarAsync);
        await Esperar(async () => await db.ContarBloqueados() > 0, "la segunda inserción espera el índice único");
        Assert.False(guardadoDeLaSegunda.IsCompleted);

        await primera.ConfirmarAsync();
        await guardadoDeLaSegunda;
        await segunda.ConfirmarAsync();

        Assert.False(primera.YaExistia);
        Assert.True(segunda.YaExistia);
        Assert.Single(await db.LeerEntregas(a.Nombre));
    }

    [Fact]
    public async Task T20_publicar_un_evento_sin_consumidores_no_escribe_filas_y_registra_Information()
    {
        var captura = new CapturaDeRegistros();
        await using var servicios = db.CrearServicios(s =>
        {
            s.AddConsumidorDePrueba<RanuraA>();
            s.AddLogging(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(captura));
        });
        await using var scope = servicios.ScopeDe(LigaA);
        await using var transaccion = await scope.Iniciar();

        scope.ServiceProvider.GetRequiredService<IPublicadorDeEventos>()
            .Publicar(new EventoSinConsumidores(LigaA, Guid.NewGuid(), 1));

        Assert.Empty(scope.Contexto().ChangeTracker.Entries<EntregaEvento>());
        Assert.Contains(captura.Entradas, e => e.Nivel == LogLevel.Information && e.Texto.Contains(EventoSinConsumidores.NombreDelEvento));
    }

    [Fact]
    public async Task T22_goalflow_app_no_puede_borrar_y_la_politica_rechaza_otro_tenant()
    {
        await using var conexion = new NpgsqlConnection(db.CadenaApp);
        await conexion.OpenAsync();

        // El permiso denegado y el rechazo por RLS comparten SQLSTATE; solo el mensaje los distingue (Spec 002 C-16).
        await using (var transaccion = await conexion.BeginTransactionAsync())
        {
            await using var borrar = new NpgsqlCommand("delete from goalflow.entregas_eventos", conexion, transaccion);
            var error = await Assert.ThrowsAsync<PostgresException>(() => borrar.ExecuteNonQueryAsync());

            Assert.Equal("42501", error.SqlState);
            Assert.StartsWith("permission denied for table entregas_eventos", error.MessageText);
        }

        await using (var transaccion = await conexion.BeginTransactionAsync())
        {
            await using (var fijar = new NpgsqlCommand("select set_config('app.tenant_id', @t, true)", conexion, transaccion))
            {
                fijar.Parameters.AddWithValue("t", LigaA.Value.ToString());
                await fijar.ExecuteNonQueryAsync();
            }

            await using var insertar = new NpgsqlCommand($"""
                insert into goalflow.entregas_eventos
                  (id, tenant_id, evento_id, tipo_de_evento, agregado_id, version_del_agregado, consumidor, estado, intentos)
                values ('{Guid.NewGuid()}', '{LigaB.Value}', '{Guid.NewGuid()}', 'x', '{Guid.NewGuid()}', 1, 'x', 'Pendiente', 0)
                """, conexion, transaccion);
            var error = await Assert.ThrowsAsync<PostgresException>(() => insertar.ExecuteNonQueryAsync());

            Assert.Equal("42501", error.SqlState);
            Assert.Contains("row-level security policy", error.MessageText);
        }
    }

    [Fact]
    public async Task T30_la_fuente_de_tenants_lista_las_ligas_sin_transaccion_ni_contexto()
    {
        var transacciones = new ContadorDeTransacciones();
        await using var servicios = db.CrearServicios(s =>
            s.ConfigureDbContext<GoalFlowDbContext>(o => o.AddInterceptors(transacciones)));
        await using var scope = servicios.CreateAsyncScope(); // Ni tenant ni identidad.

        var tenants = await scope.ServiceProvider.GetRequiredService<IFuenteDeTenants>().ListarAsync(CancellationToken.None);

        Assert.Contains(LigaA, tenants);
        Assert.Contains(LigaB, tenants);
        Assert.Equal(0, transacciones.Inicios);
    }

    [Fact]
    public async Task T31_publicar_no_llama_a_SaveChanges_y_deja_las_filas_Added()
    {
        var contador = new ContadorDeActividad();
        await using var servicios = db.CrearServicios(s =>
        {
            s.AddConsumidorDePrueba<RanuraA>();
            s.AddConsumidorDePrueba<RanuraB>();
            s.ConfigureDbContext<GoalFlowDbContext>(o => o.AddInterceptors(contador));
        });
        await using var scope = servicios.ScopeDe(LigaA);
        await using var transaccion = await scope.Iniciar();

        scope.ServiceProvider.GetRequiredService<IPublicadorDeEventos>().Publicar(new EventoDePrueba(LigaA, Guid.NewGuid(), 1));

        Assert.Equal(0, contador.Guardados);
        var entradas = scope.Contexto().ChangeTracker.Entries<EntregaEvento>().ToList();
        Assert.Equal(2, entradas.Count);
        Assert.All(entradas, e => Assert.Equal(EntityState.Added, e.State));
    }
}
