using GoalFlow.Application.Persistencia;
using GoalFlow.Application.Tenancy;
using GoalFlow.Domain.Tenancy;
using GoalFlow.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static GoalFlow.Tests.Isolation.BaseDeAislamiento;

namespace GoalFlow.Tests.Isolation;

/// <summary>Capa de base de datos: SQL crudo sin EF Core (ADR-14 §5), salvo donde T-12 prueba el interceptor real.</summary>
[Collection(ColeccionDeAislamiento.Nombre)]
public class AislamientoRlsTests(BaseDeAislamiento db)
{
    private const string PermisoDenegado = "42501";
    private const string ViolaRls = "42501";

    private static async Task<NpgsqlConnection> Abrir(string cadena)
    {
        var conexion = new NpgsqlConnection(cadena);
        await conexion.OpenAsync();
        return conexion;
    }

    private static async Task Fijar(NpgsqlConnection conexion, NpgsqlTransaction transaccion, string ajuste, Guid valor)
    {
        await using var comando = new NpgsqlCommand("select set_config(@ajuste, @valor, true)", conexion, transaccion);
        comando.Parameters.AddWithValue("ajuste", ajuste);
        comando.Parameters.AddWithValue("valor", valor.ToString());
        await comando.ExecuteNonQueryAsync();
    }

    private static async Task<List<Guid>> LeerGuids(NpgsqlConnection conexion, string sql, NpgsqlTransaction? transaccion = null)
    {
        await using var comando = new NpgsqlCommand(sql, conexion, transaccion);
        await using var lector = await comando.ExecuteReaderAsync();
        var valores = new List<Guid>();
        while (await lector.ReadAsync()) valores.Add(lector.GetGuid(0));
        return valores;
    }

    private static async Task<int> Pid(NpgsqlConnection conexion)
    {
        await using var comando = new NpgsqlCommand("select pg_backend_pid()", conexion);
        return (int)(await comando.ExecuteScalarAsync())!;
    }

    private static async Task<PostgresException> Rechazado(NpgsqlConnection conexion, string sql)
    {
        await using var comando = new NpgsqlCommand(sql, conexion);
        return await Assert.ThrowsAsync<PostgresException>(() => comando.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task T09_con_tenant_A_solo_se_ven_filas_de_A()
    {
        await using var conexion = await Abrir(db.CadenaApp);
        await using var transaccion = await conexion.BeginTransactionAsync();
        await Fijar(conexion, transaccion, "app.tenant_id", LigaA.Value);

        var tenants = await LeerGuids(conexion, "select tenant_id from goalflow.equipos", transaccion);

        Assert.NotEmpty(tenants);
        Assert.All(tenants, t => Assert.Equal(LigaA.Value, t));
    }

    [Fact]
    public async Task T10_canario_el_superusuario_ve_las_dos_ligas()
    {
        await using var conexion = await Abrir(db.CadenaSuperusuario);

        var tenants = await LeerGuids(conexion, "select tenant_id from goalflow.equipos");

        Assert.Contains(LigaA.Value, tenants);
        Assert.Contains(LigaB.Value, tenants);
    }

    [Fact]
    public async Task T11_insertar_con_tenant_ajeno_lo_rechaza_la_base()
    {
        await using var conexion = await Abrir(db.CadenaApp);
        await using var transaccion = await conexion.BeginTransactionAsync();
        await Fijar(conexion, transaccion, "app.tenant_id", LigaA.Value);

        await using var comando = new NpgsqlCommand(
            $"insert into goalflow.equipos (id, tenant_id, nombre) values ('{Guid.NewGuid()}', '{LigaB.Value}', 'equipo-ajeno')",
            conexion, transaccion);
        var error = await Assert.ThrowsAsync<PostgresException>(() => comando.ExecuteNonQueryAsync());

        Assert.Equal(ViolaRls, error.SqlState);
    }

    /// <summary>Pasa por el interceptor real sobre una conexión ajena, para que is_local = false lo delate.</summary>
    private async Task LeerComoLigaA(NpgsqlConnection conexion)
    {
        await using var servicios = db.CrearServicios();
        await using var scope = servicios.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().Establecer(LigaA);
        var contexto = scope.ServiceProvider.GetRequiredService<GoalFlowDbContext>();
        contexto.Database.SetDbConnection(conexion, contextOwnsConnection: false);

        await using var transaccion = await scope.ServiceProvider
            .GetRequiredService<IUnidadDeTrabajo>().IniciarAsync(CancellationToken.None);
        var equipos = await contexto.Equipos.ToListAsync();
        await transaccion.ConfirmarAsync(CancellationToken.None);

        Assert.Contains(equipos, e => e.Nombre == EquipoDeA); // Precondición.
    }

    [Fact]
    public async Task T12_conexion_abierta_tras_confirmar_no_ve_filas_ni_da_error()
    {
        await using var conexion = await Abrir(db.CadenaApp);
        var pid = await Pid(conexion);

        await LeerComoLigaA(conexion);

        Assert.Equal(pid, await Pid(conexion));
        Assert.Empty(await LeerGuids(conexion, "select tenant_id from goalflow.equipos"));
    }

    [Fact]
    public async Task T12_reabrir_sin_reset_de_sesion_no_ve_filas_ni_da_error()
    {
        var cadena = new NpgsqlConnectionStringBuilder(db.CadenaApp)
        {
            MaxPoolSize = 1,
            NoResetOnClose = true,
        }.ConnectionString;

        await using var conexion = await Abrir(cadena);
        var pid = await Pid(conexion);

        await LeerComoLigaA(conexion);
        await conexion.CloseAsync();
        await conexion.OpenAsync();

        Assert.Equal(pid, await Pid(conexion));
        Assert.Empty(await LeerGuids(conexion, "select tenant_id from goalflow.equipos"));
    }

    [Theory]
    [InlineData("insert into goalflow.ligas (id, slug, nombre) values (gen_random_uuid(), 'liga-x', 'Liga X')")]
    [InlineData("update goalflow.ligas set nombre = 'Otro'")]
    [InlineData("delete from goalflow.ligas")]
    [InlineData("insert into goalflow.admins_liga (user_id, tenant_id, rol) values (gen_random_uuid(), gen_random_uuid(), 'admin')")]
    [InlineData("update goalflow.admins_liga set rol = 'otro'")]
    [InlineData("delete from goalflow.admins_liga")]
    [InlineData("select * from goalflow.\"__EFMigrationsHistory\"")]
    [InlineData("delete from goalflow.\"__EFMigrationsHistory\"")]
    public async Task T13_goalflow_app_no_escribe_tablas_globales_ni_toca_el_historial(string sql)
    {
        await using var conexion = await Abrir(db.CadenaApp);

        var error = await Rechazado(conexion, sql);

        Assert.Equal(PermisoDenegado, error.SqlState);
    }

    [Fact]
    public async Task T13_goalflow_app_lee_ligas()
    {
        await using var conexion = await Abrir(db.CadenaApp);

        var ligas = await LeerGuids(conexion, "select id from goalflow.ligas");

        Assert.Contains(LigaA.Value, ligas);
        Assert.Contains(LigaB.Value, ligas);
    }

    [Fact]
    public async Task T14_admins_liga_solo_muestra_las_filas_de_la_identidad()
    {
        await using var conexion = await Abrir(db.CadenaApp);

        Assert.Empty(await LeerGuids(conexion, "select user_id from goalflow.admins_liga"));

        await using var transaccion = await conexion.BeginTransactionAsync();
        await Fijar(conexion, transaccion, "app.user_id", UsuarioU1);
        var usuarios = await LeerGuids(conexion, "select user_id from goalflow.admins_liga", transaccion);

        Assert.Equal([UsuarioU1], usuarios);
    }
}
