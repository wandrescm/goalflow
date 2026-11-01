using GoalFlow.Domain.Tenancy;
using GoalFlow.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace GoalFlow.Tests.Isolation;

[CollectionDefinition(Nombre)]
public class ColeccionDeAislamiento : ICollectionFixture<BaseDeAislamiento>
{
    public const string Nombre = "Aislamiento";
}

/// <summary>
/// Un contenedor PostgreSQL 17 para todas las pruebas de aislamiento. La preparación corre como
/// superusuario; las pruebas se conectan como goalflow_app, salvo T-10. Sin Docker, la prueba falla.
/// </summary>
public sealed class BaseDeAislamiento : IAsyncLifetime
{
    public static readonly TenantId LigaA = new(Guid.NewGuid());
    public static readonly TenantId LigaB = new(Guid.NewGuid());
    public static readonly Guid UsuarioU1 = Guid.NewGuid();
    public static readonly Guid UsuarioU2 = Guid.NewGuid();
    public const string EquipoDeA = "equipo-a-1";
    public const string EquipoDeB = "equipo-b-1";

    private readonly PostgreSqlContainer _contenedor = new PostgreSqlBuilder("postgres:17")
        .WithUsername("postgres")
        .WithPassword(Convert.ToHexString(Guid.NewGuid().ToByteArray()))
        .Build();

    public string CadenaSuperusuario => _contenedor.GetConnectionString();

    public string CadenaApp { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await _contenedor.StartAsync();

        var contrasena = Convert.ToHexString(Guid.NewGuid().ToByteArray());
        await EjecutarComoSuperusuario(await File.ReadAllTextAsync(RutaDelScriptDeRoles()));
        await EjecutarComoSuperusuario($"alter role goalflow_app password '{contrasena}'");

        var opciones = new DbContextOptionsBuilder<GoalFlowDbContext>();
        RegistroDePersistencia.ConfigurarNpgsql(opciones, CadenaSuperusuario);
        await using (var contexto = new GoalFlowDbContext(opciones.Options, new ContextoDeTenant()))
        {
            await contexto.Database.MigrateAsync();
        }

        await EjecutarComoSuperusuario($"""
            insert into goalflow.ligas (id, slug, nombre) values
              ('{LigaA.Value}', 'liga-a', 'Liga A'),
              ('{LigaB.Value}', 'liga-b', 'Liga B');
            insert into goalflow.equipos (id, tenant_id, nombre) values
              ('{Guid.NewGuid()}', '{LigaA.Value}', '{EquipoDeA}'),
              ('{Guid.NewGuid()}', '{LigaB.Value}', '{EquipoDeB}');
            insert into goalflow.admins_liga (user_id, tenant_id, rol) values
              ('{UsuarioU1}', '{LigaA.Value}', 'admin'),
              ('{UsuarioU2}', '{LigaB.Value}', 'admin');
            """);

        CadenaApp = new NpgsqlConnectionStringBuilder(CadenaSuperusuario)
        {
            Username = "goalflow_app",
            Password = contrasena,
        }.ConnectionString;
    }

    public Task DisposeAsync() => _contenedor.DisposeAsync().AsTask();

    /// <summary>El registro real de DI, conectado como goalflow_app.</summary>
    public ServiceProvider CrearServicios(Action<IServiceCollection>? ajustar = null)
    {
        var configuracion = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:GoalFlow"] = CadenaApp })
            .Build();

        var servicios = new ServiceCollection().AddSingleton<IConfiguration>(configuracion);
        servicios.AddGoalFlowPersistencia();
        ajustar?.Invoke(servicios);
        return servicios.BuildServiceProvider(validateScopes: true);
    }

    public async Task EjecutarComoSuperusuario(string sql)
    {
        await using var conexion = new NpgsqlConnection(CadenaSuperusuario);
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand(sql, conexion);
        await comando.ExecuteNonQueryAsync();
    }

    public async Task<long> ContarComoSuperusuario(string nombreDeEquipo)
    {
        await using var conexion = new NpgsqlConnection(CadenaSuperusuario);
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand("select count(*) from goalflow.equipos where nombre = @nombre", conexion);
        comando.Parameters.AddWithValue("nombre", nombreDeEquipo);
        return (long)(await comando.ExecuteScalarAsync())!;
    }

    private static string RutaDelScriptDeRoles()
    {
        var directorio = new DirectoryInfo(AppContext.BaseDirectory);
        while (directorio is not null && !File.Exists(Path.Combine(directorio.FullName, "GoalFlow.sln")))
            directorio = directorio.Parent;

        return Path.Combine(
            directorio?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio."),
            "db", "roles", "001_roles.sql");
    }
}
