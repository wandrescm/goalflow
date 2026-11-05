using System.Reflection;
using GoalFlow.Infrastructure.Persistencia;
using GoalFlow.Tests.Isolation;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace GoalFlow.Tests.Architecture;

/// <summary>Spec 004 T-25 a T-29: reglas del pipeline post-guardado, cada una con su canario.</summary>
public class PipelineArquitecturaTests
{
    private const string RaizFixtures = "GoalFlow.Tests.Architecture.Fixtures";
    private static readonly Assembly EnsambladoDePruebas = typeof(PipelineArquitecturaTests).Assembly;

    private static readonly Assembly[] Produccion =
    [
        GoalFlow.Domain.AssemblyReference.Assembly,
        GoalFlow.Application.AssemblyReference.Assembly,
        GoalFlow.Infrastructure.AssemblyReference.Assembly,
        GoalFlow.Api.AssemblyReference.Assembly,
    ];

    private static IEnumerable<Type> TiposEn(string raiz) =>
        EnsambladoDePruebas.GetTypes().Where(t => t.Namespace?.StartsWith(raiz, StringComparison.Ordinal) == true);

    internal static void Falla(ResultadoRegla resultado, string esperado)
    {
        Assert.False(resultado.Cumple, "El canario debía hacer fallar la regla");
        Assert.Contains(esperado, Assert.Single(resultado.Violaciones));
    }

    // ---- T-25: contratos de eventos (R-39, R-02) ----

    [Fact] // La corrida de producción es vacía hasta H12 (Spec 004 C-07); solo vale junto a los canarios.
    public void T25_los_eventos_de_produccion_cumplen_el_contrato()
    {
        var resultado = ReglasDeArquitectura.ContratosDeEventos(
            Produccion.SelectMany(a => a.GetTypes()), TablaDeModulos.RaicesDeCapa);

        Assert.True(resultado.Cumple, resultado.ToString());
    }

    [Fact] // Control positivo: la regla acepta un evento correcto en .Contracts.
    public void T25_control_un_evento_valido_cumple()
    {
        var raiz = $"{RaizFixtures}.EventosValidos";
        var tipos = TiposEn(raiz).ToList();

        Assert.Contains(tipos, t => t.Name == "EventoValido");
        Assert.True(ReglasDeArquitectura.ContratosDeEventos(tipos, [raiz]).Cumple);
    }

    [Theory] // Cada fixture falla por su cuenta.
    [InlineData("EventosConDatos", "EventoConDatos")]
    [InlineData("EventosFueraDeContracts", "EventoFueraDeContracts")]
    [InlineData("EventosConVersionTexto", "EventoConVersionTexto")]
    [InlineData("EventosConAgregadoEntero", "EventoConAgregadoEntero")]
    public void T25_canario_evento_que_viola_el_contrato(string espacio, string tipo)
    {
        var raiz = $"{RaizFixtures}.{espacio}";

        Falla(ReglasDeArquitectura.ContratosDeEventos(TiposEn(raiz), [raiz]), tipo);
    }

    // ---- T-26: filtro Tenant en el modelo real (R-40) ----

    private static readonly string[] ExcepcionesDelFiltro = [nameof(GoalFlow.Domain.Ligas.AdminLiga)];

    [Fact]
    public void T26_toda_entidad_con_TenantId_del_modelo_real_tiene_el_filtro()
    {
        var opciones = new DbContextOptionsBuilder<GoalFlowDbContext>();
        RegistroDePersistencia.ConfigurarNpgsql(opciones, "Host=no-se-conecta");
        using var contexto = new GoalFlowDbContext(opciones.Options, new ContextoDeTenant());

        var resultado = ReglasDeArquitectura.EntidadesConTenantTienenFiltro(contexto.Model, ExcepcionesDelFiltro);

        Assert.True(resultado.Cumple, resultado.ToString());
        Assert.Contains(contexto.Model.GetEntityTypes(), e => e.ClrType == typeof(GoalFlow.Domain.Torneos.Equipo));
    }

    [Fact]
    public void T26_canario_entidad_con_TenantId_sin_filtro()
    {
        var opciones = new DbContextOptionsBuilder<Fixtures.ModeloSinFiltro.ContextoSinFiltro>()
            .UseNpgsql("Host=no-se-conecta").Options;
        using var contexto = new Fixtures.ModeloSinFiltro.ContextoSinFiltro(opciones);

        var resultado = ReglasDeArquitectura.EntidadesConTenantTienenFiltro(contexto.Model, ExcepcionesDelFiltro);

        Falla(resultado, "EntidadConTenantSinFiltro");
    }

    // ---- T-28: cachés con SizeLimit (R-42) ----

    [Fact]
    public void T28_las_caches_de_la_API_real_tienen_SizeLimit()
    {
        IServiceCollection? registros = null;
        using var fabrica = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b => b.ConfigureServices(s => registros = s));

        var proveedor = fabrica.Services;
        var resultado = ReglasDeArquitectura.CachesConLimite(registros ?? throw new InvalidOperationException(), proveedor);

        Assert.True(resultado.Cumple, resultado.ToString());
    }

    [Fact]
    public void T28_canario_cache_sin_SizeLimit()
    {
        var servicios = new ServiceCollection().AddMemoryCache();
        using var proveedor = servicios.BuildServiceProvider();

        Falla(ReglasDeArquitectura.CachesConLimite(servicios, proveedor), "SizeLimit");
    }

    [Fact]
    public void T28_control_cache_con_SizeLimit_cumple()
    {
        var servicios = new ServiceCollection().AddMemoryCache(o => o.SizeLimit = 1024);
        using var proveedor = servicios.BuildServiceProvider();

        Assert.NotEmpty(servicios.Where(d => d.ServiceType == typeof(IMemoryCache)));
        Assert.True(ReglasDeArquitectura.CachesConLimite(servicios, proveedor).Cumple);
    }

    // ---- T-29: Mensajeria es un namespace compartido (R-01) ----

    [Fact]
    public void T29_Mensajeria_es_namespace_compartido_y_el_grafo_de_produccion_cumple()
    {
        Assert.Contains("Mensajeria", TablaDeModulos.NamespacesCompartidos);
        Assert.DoesNotContain("Mensajeria", TablaDeModulos.Adr03.Keys);

        var resultado = ReglasDeArquitectura.GrafoDeModulosEnProduccion(
            TablaDeModulos.Adr03,
            Produccion.SelectMany(a => a.GetTypes()),
            TablaDeModulos.RaicesDeCapa,
            TablaDeModulos.NamespacesCompartidos);

        Assert.True(resultado.Cumple, resultado.ToString());
        Assert.Contains(Produccion.SelectMany(a => a.GetTypes()), t => t.Namespace == "GoalFlow.Application.Mensajeria");
    }

    [Fact]
    public void T29_canario_usar_Mensajeria_no_permite_tocar_la_entidad_de_otro_modulo()
    {
        var raiz = $"{RaizFixtures}.GrafoConMensajeria";

        var resultado = ReglasDeArquitectura.GrafoDeModulosEnProduccion(
            TablaDeModulos.Adr03, TiposEn(raiz), [raiz], TablaDeModulos.NamespacesCompartidos);

        Falla(resultado, "EstadisticasViolaConMensajeria");
    }
}

/// <summary>Spec 004 T-27 (R-41): consulta al catálogo de la base migrada.</summary>
[Collection(ColeccionDeAislamiento.Nombre)]
public class TablasConRlsTests(BaseDeAislamiento db)
{
    private const string ConsultaDeCatalogo = """
        select c.relname, c.relrowsecurity, (select count(*) from pg_policy p where p.polrelid = c.oid)
        from pg_class c
        join pg_namespace n on n.oid = c.relnamespace
        join pg_attribute a on a.attrelid = c.oid and a.attname = 'tenant_id' and not a.attisdropped
        where n.nspname = 'goalflow' and c.relkind in ('r', 'p')
        """;

    private static async Task<List<ReglasDeArquitectura.FilaDeCatalogo>> Leer(NpgsqlConnection conexion, NpgsqlTransaction? transaccion)
    {
        await using var comando = new NpgsqlCommand(ConsultaDeCatalogo, conexion, transaccion);
        await using var lector = await comando.ExecuteReaderAsync();
        var filas = new List<ReglasDeArquitectura.FilaDeCatalogo>();
        while (await lector.ReadAsync())
            filas.Add(new(lector.GetString(0), lector.GetBoolean(1), lector.GetInt64(2)));
        return filas;
    }

    [Fact]
    public async Task T27_toda_tabla_goalflow_con_tenant_id_tiene_RLS_y_politica()
    {
        await using var conexion = new NpgsqlConnection(db.CadenaSuperusuario);
        await conexion.OpenAsync();

        var filas = await Leer(conexion, null);

        Assert.Contains(filas, f => f.Tabla == "equipos");
        Assert.Contains(filas, f => f.Tabla == "admins_liga");
        Assert.True(ReglasDeArquitectura.TablasConTenantTienenRls(filas).Cumple);
    }

    [Fact]
    public async Task T27_canario_tabla_con_tenant_id_sin_RLS_creada_en_una_transaccion_revertida()
    {
        await using var conexion = new NpgsqlConnection(db.CadenaSuperusuario);
        await conexion.OpenAsync();
        await using var transaccion = await conexion.BeginTransactionAsync();
        await using (var ddl = new NpgsqlCommand("create table goalflow.canario_sin_rls (tenant_id uuid)", conexion, transaccion))
            await ddl.ExecuteNonQueryAsync();

        PipelineArquitecturaTests.Falla(
            ReglasDeArquitectura.TablasConTenantTienenRls(await Leer(conexion, transaccion)),
            "canario_sin_rls tiene tenant_id y no tiene RLS");

        await using (var rls = new NpgsqlCommand("alter table goalflow.canario_sin_rls enable row level security", conexion, transaccion))
            await rls.ExecuteNonQueryAsync();

        PipelineArquitecturaTests.Falla(
            ReglasDeArquitectura.TablasConTenantTienenRls(await Leer(conexion, transaccion)),
            "canario_sin_rls tiene RLS pero ninguna política");
        await transaccion.RollbackAsync();
    }
}
