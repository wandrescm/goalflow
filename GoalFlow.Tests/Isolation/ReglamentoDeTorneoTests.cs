using Npgsql;
using static GoalFlow.Tests.Isolation.BaseDeAislamiento;

namespace GoalFlow.Tests.Isolation;

/// <summary>
/// Spec 009 T-14 (parte del reglamento: R-01, R-02, R-34): RLS, clave foránea compuesta y privilegios de
/// <c>reglamentos_torneo</c>. SQL crudo como goalflow_app contra la base desechable (lista permitida de ADR-14 C-10);
/// toda sentencia corre en una transacción que se revierte. Reutiliza el grafo de torneos de las pruebas de la planilla.
/// </summary>
[Collection(ColeccionDeAislamiento.Nombre)]
public class ReglamentoDeTorneoTests(BaseDeAislamiento db)
{
    private const string ViolaClaveForanea = "23503";
    private const string ViolaUnicidad = "23505";
    private const string ViolaCheck = "23514";
    private const string PermisoDenegado = "42501";

    private static readonly SemaphoreSlim Candado = new(1, 1);
    private static readonly HashSet<string> Sembradas = [];

    private const string Contenido = """'{"puntos": {"victoria": 3, "empate": 1, "derrota": 0}}'""";

    private static string Insertar(Guid tenant, Guid torneo, int version, string estado = "Provisional", string contenido = Contenido) => $"""
        insert into goalflow.reglamentos_torneo (id, tenant_id, torneo_id, version, estado, contenido, guardado_por)
        values ('{Guid.NewGuid()}', '{tenant}', '{torneo}', {version}, '{estado}', {contenido}::jsonb, '{UsuarioU1}')
        """;

    /// <summary>Una versión 1 por liga, sembrada una sola vez como superusuario (preparación, no una prueba de aislamiento).</summary>
    private async Task<GrafoDePlanillas> Sembrar()
    {
        var g = await GrafoDePlanillas.ObtenerAsync(db);
        await Candado.WaitAsync();
        try
        {
            if (Sembradas.Add(db.CadenaSuperusuario))
                await db.EjecutarComoSuperusuario($"{Insertar(LigaA.Value, g.TorneoA, 1)}; {Insertar(LigaB.Value, g.TorneoB, 1)};");
        }
        finally
        {
            Candado.Release();
        }

        return g;
    }

    private static async Task Ejecutar(NpgsqlConnection conexion, NpgsqlTransaction transaccion, string sql)
    {
        await using var comando = new NpgsqlCommand(sql, conexion, transaccion);
        await comando.ExecuteNonQueryAsync();
    }

    private async Task<(NpgsqlConnection Conexion, NpgsqlTransaction Transaccion)> TransaccionDeLaLigaA()
    {
        var conexion = new NpgsqlConnection(db.CadenaApp);
        await conexion.OpenAsync();
        var transaccion = await conexion.BeginTransactionAsync();
        await Ejecutar(conexion, transaccion, $"select set_config('app.tenant_id', '{LigaA.Value}', true)");
        return (conexion, transaccion);
    }

    private async Task<PostgresException> RechazadoParaLigaA(string sql)
    {
        var (conexion, transaccion) = await TransaccionDeLaLigaA();
        await using var _ = conexion;
        await using var __ = transaccion;
        return await Assert.ThrowsAsync<PostgresException>(() => Ejecutar(conexion, transaccion, sql));
    }

    [Fact] // R-02: la política del tenant; sin tenant, cero filas
    public async Task R02_la_tabla_solo_muestra_las_filas_del_tenant_de_la_transaccion()
    {
        await Sembrar();
        await using var conexion = new NpgsqlConnection(db.CadenaApp);
        await conexion.OpenAsync();

        async Task<(long Propias, long Ajenas)> Contar(Guid? tenant)
        {
            await using var transaccion = await conexion.BeginTransactionAsync();
            if (tenant is not null) await Ejecutar(conexion, transaccion, $"select set_config('app.tenant_id', '{tenant}', true)");
            var referencia = tenant ?? Guid.Empty;
            await using var comando = new NpgsqlCommand($"""
                select count(*) filter (where tenant_id = '{referencia}'), count(*) filter (where tenant_id <> '{referencia}')
                from goalflow.reglamentos_torneo
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

        Assert.True(propiasA >= 1 && propiasB >= 1, "Las dos ligas tienen un reglamento sembrado");
        Assert.Equal(0, ajenasA);
        Assert.Equal(0, ajenasB);
        Assert.Equal((0, 0), sinTenant);
    }

    [Fact] // T-14: un reglamento de la liga A que referencia un torneo de la liga B es rechazado con 23503
    public async Task T14_un_reglamento_de_la_liga_A_sobre_un_torneo_de_la_liga_B_es_rechazado_con_23503()
    {
        var g = await Sembrar();

        var error = await RechazadoParaLigaA(Insertar(LigaA.Value, g.TorneoB, 2));

        Assert.Equal(ViolaClaveForanea, error.SqlState);
        Assert.Equal("fk_reglamentos_torneo_torneos", error.ConstraintName);
    }

    [Fact] // R-02: la política WITH CHECK impide escribir una fila de otro tenant
    public async Task R02_insertar_una_fila_de_otro_tenant_es_rechazado_por_la_politica()
    {
        var g = await Sembrar();

        var error = await RechazadoParaLigaA(Insertar(LigaB.Value, g.TorneoB, 2));

        Assert.Equal("42501", error.SqlState);
        Assert.Contains("row-level security", error.MessageText, StringComparison.Ordinal);
    }

    [Theory] // T-14, R-02: goalflow_app no tiene UPDATE ni DELETE
    [InlineData("update goalflow.reglamentos_torneo set version = version")]
    [InlineData("delete from goalflow.reglamentos_torneo")]
    public async Task T14_UPDATE_y_DELETE_sobre_el_reglamento_son_rechazados_por_permiso(string sql)
    {
        await Sembrar();

        var error = await RechazadoParaLigaA(sql);

        Assert.Equal(PermisoDenegado, error.SqlState);
        Assert.StartsWith("permission denied for table", error.MessageText, StringComparison.Ordinal);
    }

    [Fact] // R-02: SELECT e INSERT sí funcionan dentro de una transacción del tenant
    public async Task R02_INSERT_funciona_dentro_de_una_transaccion_del_tenant()
    {
        var g = await Sembrar();
        var (conexion, transaccion) = await TransaccionDeLaLigaA();
        await using var _ = conexion;
        await using var __ = transaccion;

        await Ejecutar(conexion, transaccion, Insertar(LigaA.Value, g.TorneoA, 2, "Confirmado"));
    }

    [Fact] // R-01: (tenant, torneo, versión) es único
    public async Task R01_repetir_la_version_de_un_torneo_es_rechazado_con_23505()
    {
        var g = await Sembrar();

        var error = await RechazadoParaLigaA(Insertar(LigaA.Value, g.TorneoA, 1));

        Assert.Equal(ViolaUnicidad, error.SqlState);
        Assert.Equal("ux_reglamentos_torneo_tenant_torneo_version", error.ConstraintName);
    }

    [Theory] // R-01: versión ≥ 1, estado de la lista cerrada y contenido objeto
    [InlineData(0, "Provisional", Contenido, "ck_reglamentos_torneo_version")]
    [InlineData(3, "Oficial", Contenido, "ck_reglamentos_torneo_estado")]
    [InlineData(3, "Provisional", "'[1]'", "ck_reglamentos_torneo_contenido")]
    public async Task R01_los_check_rechazan_version_estado_y_contenido_invalidos(int version, string estado, string contenido, string restriccion)
    {
        var g = await Sembrar();

        var error = await RechazadoParaLigaA(Insertar(LigaA.Value, g.TorneoA, version, estado, contenido));

        Assert.Equal(ViolaCheck, error.SqlState);
        Assert.Equal(restriccion, error.ConstraintName);
    }

    [Fact] // R-02, R-34: RLS activada y privilegios exactos en el catálogo
    public async Task R02_el_catalogo_tiene_RLS_activada_y_solo_SELECT_e_INSERT_para_goalflow_app()
    {
        await using var conexion = new NpgsqlConnection(db.CadenaSuperusuario);
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand("""
            select c.relrowsecurity,
                   has_table_privilege('goalflow_app', c.oid, 'SELECT'), has_table_privilege('goalflow_app', c.oid, 'INSERT'),
                   has_table_privilege('goalflow_app', c.oid, 'UPDATE'), has_table_privilege('goalflow_app', c.oid, 'DELETE'),
                   has_table_privilege('goalflow_app', c.oid, 'TRUNCATE'),
                   (select count(*) from pg_policy p where p.polrelid = c.oid)
            from pg_class c join pg_namespace n on n.oid = c.relnamespace
            where n.nspname = 'goalflow' and c.relname = 'reglamentos_torneo'
            """, conexion);
        await using var lector = await comando.ExecuteReaderAsync();
        Assert.True(await lector.ReadAsync(), "La tabla reglamentos_torneo existe");

        Assert.True(lector.GetBoolean(0));
        Assert.True(lector.GetBoolean(1));
        Assert.True(lector.GetBoolean(2));
        Assert.False(lector.GetBoolean(3));
        Assert.False(lector.GetBoolean(4));
        Assert.False(lector.GetBoolean(5));
        Assert.Equal(1, lector.GetInt64(6));
    }
}
