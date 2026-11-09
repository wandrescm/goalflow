using GoalFlow.Domain.Torneos;
using GoalFlow.Infrastructure.Persistencia;
using GoalFlow.Tests.Isolation;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GoalFlow.Tests.Architecture;

/// <summary>Spec 005 T-23 (R-35) y T-25 (R-34): reglas sobre el modelo real y conjuntos exactos.</summary>
public class ModeloDeTorneosArquitecturaTests
{
    private static GoalFlowDbContext ContextoReal()
    {
        var opciones = new DbContextOptionsBuilder<GoalFlowDbContext>();
        RegistroDePersistencia.ConfigurarNpgsql(opciones, "Host=no-se-conecta");
        return new GoalFlowDbContext(opciones.Options, new ContextoDeTenant());
    }

    private static readonly string[] ExcepcionesDelFiltro = [nameof(GoalFlow.Domain.Ligas.AdminLiga)];

    [Fact] // T-23
    public void T23_el_modelo_real_cumple_las_reglas_de_claves_y_unicidad_con_tenant()
    {
        using var contexto = ContextoReal();

        var resultado = ReglasDeArquitectura.ClavesYUnicidadIncluyenTenant(contexto.Model);

        Assert.True(resultado.Cumple, resultado.ToString());
        // Anti-vacío: la regla ve las entidades de torneo, sus claves foráneas compuestas y sus claves alternas.
        var tipos = contexto.Model.GetEntityTypes().Select(e => e.ClrType).ToList();
        Assert.Contains(typeof(Partido), tipos);
        Assert.Contains(contexto.Model.FindEntityType(typeof(Partido))!.GetForeignKeys(), fk => fk.Properties.Count >= 3);
        Assert.Contains(contexto.Model.FindEntityType(typeof(Torneo))!.GetKeys(), k => !k.IsPrimaryKey());
    }

    [Fact] // T-23 (canario)
    public void T23_canario_una_FK_simple_entre_entidades_con_tenant()
    {
        var opciones = new DbContextOptionsBuilder<Fixtures.ModeloConFkSimple.ContextoConFkSimple>()
            .UseNpgsql("Host=no-se-conecta").Options;
        using var contexto = new Fixtures.ModeloConFkSimple.ContextoConFkSimple(opciones);

        var resultado = ReglasDeArquitectura.ClavesYUnicidadIncluyenTenant(contexto.Model);

        var violacion = Assert.Single(resultado.Violaciones);
        Assert.Contains("clave foránea", violacion);
    }

    [Fact] // T-23 (canario)
    public void T23_canario_un_indice_unico_sin_TenantId()
    {
        var opciones = new DbContextOptionsBuilder<Fixtures.ModeloConIndiceSinTenant.ContextoConIndiceSinTenant>()
            .UseNpgsql("Host=no-se-conecta").Options;
        using var contexto = new Fixtures.ModeloConIndiceSinTenant.ContextoConIndiceSinTenant(opciones);

        var resultado = ReglasDeArquitectura.ClavesYUnicidadIncluyenTenant(contexto.Model);

        var violacion = Assert.Single(resultado.Violaciones);
        Assert.Contains("índice único", violacion);
    }

    [Fact] // T-23 (canario de las claves alternas)
    public void T23_canario_una_clave_alterna_sin_TenantId()
    {
        var opciones = new DbContextOptionsBuilder<Fixtures.ModeloConClaveAlternaSinTenant.ContextoConClaveAlterna>()
            .UseNpgsql("Host=no-se-conecta").Options;
        using var contexto = new Fixtures.ModeloConClaveAlternaSinTenant.ContextoConClaveAlterna(opciones);

        var resultado = ReglasDeArquitectura.ClavesYUnicidadIncluyenTenant(contexto.Model);

        var violacion = Assert.Single(resultado.Violaciones);
        Assert.Contains("clave alterna", violacion);
    }

    [Fact] // T-25 (Spec 004 T-26)
    public void T25_la_regla_del_filtro_evalua_exactamente_las_entidades_con_tenant_esperadas()
    {
        using var contexto = ContextoReal();

        var evaluadas = ReglasDeArquitectura.EntidadesEvaluadasPorElFiltro(contexto.Model, ExcepcionesDelFiltro)
            .Select(e => e.ClrType.Name).Order().ToArray();

        Assert.Equal(
            new[]
            {
                "Equipo", "EntregaEvento", "Torneo", "Fase", "EquipoTorneo", "Jugador", "InscripcionJugador", "Partido",
                "Planilla", "Gol", "Tarjeta",
            }.Order().ToArray(),
            evaluadas);
    }
}

/// <summary>Spec 005 T-25 (R-34): el conjunto exacto de tablas que la regla de RLS evalúa (Spec 004 T-27).</summary>
[Collection(ColeccionDeAislamiento.Nombre)]
public class ConjuntoDeTablasConTenantTests(BaseDeAislamiento db)
{
    private const string Consulta = """
        select c.relname
        from pg_class c
        join pg_namespace n on n.oid = c.relnamespace
        join pg_attribute a on a.attrelid = c.oid and a.attname = 'tenant_id' and not a.attisdropped
        where n.nspname = 'goalflow' and c.relkind in ('r', 'p')
        """;

    [Fact]
    public async Task T25_la_regla_de_RLS_evalua_exactamente_las_tablas_con_tenant_id_esperadas()
    {
        await using var conexion = new NpgsqlConnection(db.CadenaSuperusuario);
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand(Consulta, conexion);
        await using var lector = await comando.ExecuteReaderAsync();
        var tablas = new List<string>();
        while (await lector.ReadAsync()) tablas.Add(lector.GetString(0));

        Assert.Equal(
            new[]
            {
                "equipos", "entregas_eventos", "torneos", "fases", "equipos_torneo", "jugadores",
                "inscripciones_jugador", "partidos", "admins_liga", "planillas", "goles", "tarjetas",
            }.Order().ToArray(),
            tablas.Order().ToArray());
    }
}
