using GoalFlow.Tests.Isolation;
using Npgsql;

namespace GoalFlow.Tests.Architecture;

/// <summary>
/// Spec 005 T-33 (R-35, C-22): la regla de claves con tenant sobre el catálogo de la base migrada. Es una lectura del
/// catálogo como superusuario, no una prueba de aislamiento.
/// </summary>
[Collection(ColeccionDeAislamiento.Nombre)]
public class ClavesDelCatalogoTests(BaseDeAislamiento db)
{
    // Claves foráneas entre dos tablas del schema goalflow que tienen tenant_id (se excluye ligas, ADR-14 §2.1).
    private const string ConsultaDeClavesForaneas = """
        select c.conname, t.relname,
               (select array_agg(a.attname::text order by k.ord)
                  from unnest(c.conkey) with ordinality k(attnum, ord)
                  join pg_attribute a on a.attrelid = c.conrelid and a.attnum = k.attnum),
               (select array_agg(a.attname::text order by k.ord)
                  from unnest(c.confkey) with ordinality k(attnum, ord)
                  join pg_attribute a on a.attrelid = c.confrelid and a.attnum = k.attnum)
        from pg_constraint c
        join pg_class t on t.oid = c.conrelid
        join pg_namespace n on n.oid = t.relnamespace
        join pg_class r on r.oid = c.confrelid
        where c.contype = 'f' and n.nspname = 'goalflow' and r.relname <> 'ligas'
          and exists (select 1 from pg_attribute x where x.attrelid = t.oid and x.attname = 'tenant_id' and not x.attisdropped)
          and exists (select 1 from pg_attribute x where x.attrelid = r.oid and x.attname = 'tenant_id' and not x.attisdropped)
        """;

    // Índices únicos de tablas con tenant_id, salvo las claves primarias.
    private const string ConsultaDeIndicesUnicos = """
        select ic.relname, t.relname,
               (select array_agg(a.attname::text order by k.ord)
                  from unnest(i.indkey::int2[]) with ordinality k(attnum, ord)
                  join pg_attribute a on a.attrelid = i.indrelid and a.attnum = k.attnum)
        from pg_index i
        join pg_class ic on ic.oid = i.indexrelid
        join pg_class t on t.oid = i.indrelid
        join pg_namespace n on n.oid = t.relnamespace
        where i.indisunique and not i.indisprimary and n.nspname = 'goalflow'
          and exists (select 1 from pg_attribute x where x.attrelid = t.oid and x.attname = 'tenant_id' and not x.attisdropped)
        """;

    private static async Task<List<ReglasDeArquitectura.FilaDeClave>> Leer(NpgsqlConnection conexion, NpgsqlTransaction? transaccion)
    {
        var filas = new List<ReglasDeArquitectura.FilaDeClave>();

        await using (var comando = new NpgsqlCommand(ConsultaDeClavesForaneas, conexion, transaccion))
        await using (var lector = await comando.ExecuteReaderAsync())
            while (await lector.ReadAsync())
                filas.Add(new("fk", lector.GetString(0), lector.GetString(1), lector.GetFieldValue<string[]>(2), lector.GetFieldValue<string[]>(3)));

        await using (var comando = new NpgsqlCommand(ConsultaDeIndicesUnicos, conexion, transaccion))
        await using (var lector = await comando.ExecuteReaderAsync())
            while (await lector.ReadAsync())
                filas.Add(new("unico", lector.GetString(0), lector.GetString(1), lector.GetFieldValue<string[]>(2)));

        return filas;
    }

    [Fact]
    public async Task T33_las_claves_de_la_base_migrada_incluyen_tenant_id()
    {
        await using var conexion = new NpgsqlConnection(db.CadenaSuperusuario);
        await conexion.OpenAsync();

        var filas = await Leer(conexion, null);

        var resultado = ReglasDeArquitectura.ClavesDelCatalogoIncluyenTenant(filas);
        Assert.True(resultado.Cumple, resultado.ToString());

        // Anti-vacío: la regla evaluó al menos las 8 claves foráneas compuestas de la migración y sus índices únicos.
        var foraneas = filas.Where(f => f.Tipo == "fk").Select(f => f.Nombre).ToHashSet();
        foreach (var esperada in new[]
        {
            "fk_fases_torneos", "fk_equipos_torneo_torneos", "fk_equipos_torneo_equipos",
            "fk_inscripciones_jugador_equipos_torneo", "fk_inscripciones_jugador_jugadores",
            "fk_partidos_fases", "fk_partidos_local", "fk_partidos_visitante",
        })
            Assert.Contains(esperada, foraneas);
        Assert.Contains(filas, f => f.Tipo == "unico" && f.Nombre == "ux_jugadores_tenant_codigo");
        Assert.Contains(filas, f => f.Tipo == "unico" && f.Nombre == "ak_equipos_tenant_id_id");
    }

    [Fact]
    public async Task T33_canario_una_FK_simple_a_equipos_da_exactamente_una_violacion_en_una_transaccion_revertida()
    {
        await using var conexion = new NpgsqlConnection(db.CadenaSuperusuario);
        await conexion.OpenAsync();
        await using var transaccion = await conexion.BeginTransactionAsync();
        await using (var ddl = new NpgsqlCommand(
            "create table goalflow.canario_fk_tenant (tenant_id uuid, equipo_id uuid references goalflow.equipos (id))",
            conexion, transaccion))
            await ddl.ExecuteNonQueryAsync();

        var resultado = ReglasDeArquitectura.ClavesDelCatalogoIncluyenTenant(await Leer(conexion, transaccion));
        await transaccion.RollbackAsync();

        var violacion = Assert.Single(resultado.Violaciones);
        Assert.Contains("canario_fk_tenant", violacion);
    }
}
