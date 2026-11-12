using Npgsql;
using static GoalFlow.Tests.Isolation.BaseDeAislamiento;

namespace GoalFlow.Tests.Isolation;

/// <summary>
/// Filas de IA de las ligas A y B (Spec 007 R-01 a R-04), creadas una sola vez como superusuario. Se apoya en el grafo de
/// planillas (partido de A, partido de B). Los meses y las huellas son de esta clase: ninguna otra prueba los usa.
/// Datos inventados (Spec 007 §4).
/// </summary>
public sealed record GrafoDeIa(
    GrafoDePlanillas Planillas, string MesDelGrafo, string Huella,
    Guid ContadorA, Guid ContadorB, Guid LlamadaA, Guid LlamadaB, Guid ResumenA, Guid ResumenB)
{
    public const string MesFijo = "2099-01";
    public static readonly string HuellaValida = new('a', 64);

    private static readonly SemaphoreSlim Candado = new(1, 1);
    private static readonly Dictionary<string, GrafoDeIa> PorBase = [];

    public static async Task<GrafoDeIa> ObtenerAsync(BaseDeAislamiento db)
    {
        var p = await GrafoDePlanillas.ObtenerAsync(db);
        await Candado.WaitAsync();
        try
        {
            if (PorBase.TryGetValue(db.CadenaSuperusuario, out var existente)) return existente;

            var g = new GrafoDeIa(
                p, MesFijo, HuellaValida,
                Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
                Guid.CreateVersion7(), Guid.CreateVersion7());
            var (a, b) = (LigaA.Value, LigaB.Value);

            await db.EjecutarComoSuperusuario($"""
                insert into goalflow.presupuestos_ia (id, tenant_id, mes, tope, reservado, consumido) values
                  ('{g.ContadorA}', '{a}', '{MesFijo}', 1500000, 0, 0),
                  ('{g.ContadorB}', '{b}', '{MesFijo}', 1500000, 0, 0);
                insert into goalflow.llamadas_ia (id, tenant_id, torneo_id, partido_id, version, huella, numero, intento_entrega, mes, reserva, estado) values
                  ('{g.LlamadaA}', '{a}', '{p.TorneoA}', '{p.PartidoA}', 1, '{HuellaValida}', 1, 1, '{MesFijo}', 525, 'Reservada'),
                  ('{g.LlamadaB}', '{b}', '{p.TorneoB}', '{p.PartidoB}', 1, '{HuellaValida}', 1, 1, '{MesFijo}', 525, 'Reservada');
                insert into goalflow.resumenes_partido (id, tenant_id, torneo_id, partido_id, version_origen, huella, origen, estado, texto, modelo, version_prompt, llamada_id) values
                  ('{g.ResumenA}', '{a}', '{p.TorneoA}', '{p.PartidoA}', 1, '{HuellaValida}', 'Modelo', 'PorVerificar', 'Texto inventado de la liga A.', 'claude-haiku-5-5', 'a01-v1', '{g.LlamadaA}'),
                  ('{g.ResumenB}', '{b}', '{p.TorneoB}', '{p.PartidoB}', 1, '{HuellaValida}', 'Modelo', 'PorVerificar', 'Texto inventado de la liga B.', 'claude-haiku-5-5', 'a01-v1', '{g.LlamadaB}');
                """);

            PorBase[db.CadenaSuperusuario] = g;
            return g;
        }
        finally
        {
            Candado.Release();
        }
    }
}

/// <summary>
/// Spec 007 T-25 (R-01 a R-04): catálogo y modelo de las tres tablas de IA. Las inserciones inválidas corren como
/// superusuario dentro de una transacción que se revierte (ADR-14 C-10); las de claves foráneas, como goalflow_app.
/// </summary>
[Collection(ColeccionDeAislamiento.Nombre)]
public class ModeloDeIaTests(BaseDeAislamiento db)
{
    private const string ViolaClaveForanea = "23503";
    private const string ViolaUnicidad = "23505";
    private const string ViolaCheck = "23514";

    private static readonly string[] Tablas = ["resumenes_partido", "llamadas_ia", "presupuestos_ia"];

    private static async Task Ejecutar(NpgsqlConnection conexion, NpgsqlTransaction transaccion, string sql)
    {
        await using var comando = new NpgsqlCommand(sql, conexion, transaccion);
        await comando.ExecuteNonQueryAsync();
    }

    // Valores válidos por defecto; cada caso reemplaza solo lo que quiere romper. "null" es el literal SQL.
    private static Dictionary<string, string> Resumen(GrafoDeIa g, params (string Columna, string Valor)[] cambios) =>
        Mezclar(new Dictionary<string, string>
        {
            ["id"] = $"'{Guid.CreateVersion7()}'",
            ["tenant_id"] = $"'{LigaA.Value}'",
            ["torneo_id"] = $"'{g.Planillas.TorneoA}'",
            ["partido_id"] = $"'{g.Planillas.PartidoA}'",
            ["version_origen"] = "1",
            ["huella"] = $"'{new string('b', 64)}'",
            ["origen"] = "'Plantilla'",
            ["estado"] = "'PorVerificar'",
            ["motivo_borrador"] = "null",
            ["texto"] = "'Equipo Uno 2-1 Equipo Dos.'",
            ["modelo"] = "null",
            ["version_prompt"] = "null",
            ["llamada_id"] = "null",
        }, cambios);

    private static Dictionary<string, string> Llamada(GrafoDeIa g, params (string Columna, string Valor)[] cambios) =>
        Mezclar(new Dictionary<string, string>
        {
            ["id"] = $"'{Guid.CreateVersion7()}'",
            ["tenant_id"] = $"'{LigaA.Value}'",
            ["torneo_id"] = $"'{g.Planillas.TorneoA}'",
            ["partido_id"] = $"'{g.Planillas.PartidoA}'",
            ["version"] = "1",
            ["huella"] = $"'{new string('c', 64)}'",
            ["numero"] = "1",
            ["intento_entrega"] = "1",
            ["mes"] = "'2099-02'",
            ["reserva"] = "525",
            ["estado"] = "'Reservada'",
        }, cambios);

    private static Dictionary<string, string> Conciliada(GrafoDeIa g, params (string Columna, string Valor)[] cambios) =>
        Llamada(g, [("estado", "'Conciliada'"), ("tokens_entrada", "2000"), ("tokens_salida", "400"), ("costo", "400"),
            ("resultado", "'ok'"), ("conciliada_en", "now()"), .. cambios]);

    private static Dictionary<string, string> Contador(params (string Columna, string Valor)[] cambios) =>
        Mezclar(new Dictionary<string, string>
        {
            ["id"] = $"'{Guid.CreateVersion7()}'",
            ["tenant_id"] = $"'{LigaA.Value}'",
            ["mes"] = "'2099-03'",
            ["tope"] = "1500000",
            ["reservado"] = "0",
            ["consumido"] = "0",
        }, cambios);

    private static Dictionary<string, string> Mezclar(Dictionary<string, string> basica, (string Columna, string Valor)[] cambios)
    {
        foreach (var (columna, valor) in cambios) basica[columna] = valor;
        return basica;
    }

    private static string Insertar(string tabla, Dictionary<string, string> v) =>
        $"insert into goalflow.{tabla} ({string.Join(", ", v.Keys)}) values ({string.Join(", ", v.Values)})";

    private async Task<PostgresException> RechazadoComoSuperusuario(params string[] sentencias)
    {
        await using var conexion = new NpgsqlConnection(db.CadenaSuperusuario);
        await conexion.OpenAsync();
        await using var transaccion = await conexion.BeginTransactionAsync();
        for (var i = 0; i < sentencias.Length - 1; i++) await Ejecutar(conexion, transaccion, sentencias[i]);
        return await Assert.ThrowsAsync<PostgresException>(() => Ejecutar(conexion, transaccion, sentencias[^1]));
    }

    // ---- Los check constraints de R-01 a R-03: cada enumeración, rango y regla "si y solo si" rechaza una fila que la viola ----

    [Theory]
    [InlineData("version_origen = 0", "ck_resumenes_partido_version_origen", "version_origen", "0")]
    [InlineData("huella con mayúsculas", "ck_resumenes_partido_huella", "huella", "'AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA'")]
    [InlineData("huella de 63 caracteres", "ck_resumenes_partido_huella", "huella", "'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb'")]
    [InlineData("origen desconocido", "ck_resumenes_partido_origen", "origen", "'Otro'")]
    [InlineData("estado Publicado", "ck_resumenes_partido_estado", "estado", "'Publicado'")]
    [InlineData("texto vacío", "ck_resumenes_partido_texto", "texto", "''")]
    public async Task T25_los_checks_de_un_solo_campo_de_resumenes_rechazan_con_23514(
        string caso, string restriccion, string columna, string valor)
    {
        Assert.NotEmpty(caso);
        var g = await GrafoDeIa.ObtenerAsync(db);

        var error = await RechazadoComoSuperusuario(Insertar("resumenes_partido", Resumen(g, (columna, valor))));

        Assert.Equal(ViolaCheck, error.SqlState);
        Assert.Equal(restriccion, error.ConstraintName);
    }

    [Fact]
    public async Task T25_un_texto_de_4001_caracteres_es_rechazado_y_uno_de_4000_es_aceptado()
    {
        var g = await GrafoDeIa.ObtenerAsync(db);

        var error = await RechazadoComoSuperusuario(
            Insertar("resumenes_partido", Resumen(g, ("texto", $"'{new string('x', 4001)}'"))));
        Assert.Equal(ViolaCheck, error.SqlState);
        Assert.Equal("ck_resumenes_partido_texto", error.ConstraintName);

        await using var conexion = new NpgsqlConnection(db.CadenaSuperusuario);
        await conexion.OpenAsync();
        await using var transaccion = await conexion.BeginTransactionAsync();
        await Ejecutar(conexion, transaccion, Insertar("resumenes_partido", Resumen(g, ("texto", $"'{new string('x', 4000)}'"))));
    }

    [Theory]
    [InlineData("motivo fuera del catálogo", "ck_resumenes_partido_motivo", "'Borrador'", "'otro'")]
    [InlineData("Borrador sin motivo", "ck_resumenes_partido_motivo_segun_estado", "'Borrador'", "null")]
    [InlineData("PorVerificar con motivo", "ck_resumenes_partido_motivo_segun_estado", "'PorVerificar'", "'presupuesto'")]
    public async Task T25_el_motivo_del_borrador_existe_si_y_solo_si_el_estado_es_Borrador(
        string caso, string restriccion, string estado, string motivo)
    {
        Assert.NotEmpty(caso);
        var g = await GrafoDeIa.ObtenerAsync(db);

        var error = await RechazadoComoSuperusuario(
            Insertar("resumenes_partido", Resumen(g, ("estado", estado), ("motivo_borrador", motivo))));

        Assert.Equal(ViolaCheck, error.SqlState);
        Assert.Equal(restriccion, error.ConstraintName);
    }

    [Theory]
    [InlineData("Modelo sin modelo", "ck_resumenes_partido_modelo_segun_origen", "modelo", "null")]
    [InlineData("Modelo sin version_prompt", "ck_resumenes_partido_version_prompt_segun_origen", "version_prompt", "null")]
    [InlineData("Modelo sin llamada", "ck_resumenes_partido_llamada_segun_origen", "llamada_id", "null")]
    [InlineData("Plantilla con modelo", "ck_resumenes_partido_modelo_segun_origen", "modelo", "'claude-haiku-5-5'")]
    [InlineData("Plantilla con version_prompt", "ck_resumenes_partido_version_prompt_segun_origen", "version_prompt", "'a01-v1'")]
    [InlineData("Plantilla con llamada", "ck_resumenes_partido_llamada_segun_origen", "llamada_id", "'00000000-0000-0000-0000-000000000001'")]
    public async Task T25_modelo_version_prompt_y_llamada_existen_si_y_solo_si_el_origen_es_Modelo(
        string caso, string restriccion, string columna, string valor)
    {
        var g = await GrafoDeIa.ObtenerAsync(db);
        var comoModelo = caso.StartsWith("Modelo", StringComparison.Ordinal);
        (string, string)[] cambios = comoModelo
            ? [("origen", "'Modelo'"), ("modelo", "'claude-haiku-5-5'"), ("version_prompt", "'a01-v1'"),
                ("llamada_id", $"'{g.LlamadaA}'"), (columna, valor)]
            : [(columna, valor)];

        var error = await RechazadoComoSuperusuario(Insertar("resumenes_partido", Resumen(g, cambios)));

        Assert.Equal(ViolaCheck, error.SqlState);
        Assert.Equal(restriccion, error.ConstraintName);
    }

    [Theory]
    [InlineData("numero 0", "ck_llamadas_ia_numero", "numero", "0")]
    [InlineData("intento 0", "ck_llamadas_ia_intento_entrega", "intento_entrega", "0")]
    [InlineData("mes con mes 13", "ck_llamadas_ia_mes", "mes", "'2099-13'")]
    [InlineData("mes sin cero", "ck_llamadas_ia_mes", "mes", "'2099-1'")]
    [InlineData("reserva 0", "ck_llamadas_ia_reserva", "reserva", "0")]
    [InlineData("estado desconocido", "ck_llamadas_ia_estado", "estado", "'Perdida'")]
    [InlineData("version 0", "ck_llamadas_ia_version", "version", "0")]
    public async Task T25_los_checks_de_un_solo_campo_de_llamadas_rechazan_con_23514(
        string caso, string restriccion, string columna, string valor)
    {
        Assert.NotEmpty(caso);
        var g = await GrafoDeIa.ObtenerAsync(db);

        var error = await RechazadoComoSuperusuario(Insertar("llamadas_ia", Llamada(g, (columna, valor))));

        Assert.Equal(ViolaCheck, error.SqlState);
        Assert.Equal(restriccion, error.ConstraintName);
    }

    [Theory]
    [InlineData("Conciliada sin tokens de entrada", "ck_llamadas_ia_tokens_entrada_segun_estado", "tokens_entrada")]
    [InlineData("Conciliada sin tokens de salida", "ck_llamadas_ia_tokens_salida_segun_estado", "tokens_salida")]
    [InlineData("Conciliada sin costo", "ck_llamadas_ia_costo_segun_estado", "costo")]
    [InlineData("Conciliada sin resultado", "ck_llamadas_ia_resultado_segun_estado", "resultado")]
    [InlineData("Conciliada sin hora", "ck_llamadas_ia_conciliada_en_segun_estado", "conciliada_en")]
    public async Task T25_los_datos_de_conciliacion_existen_si_y_solo_si_la_llamada_esta_Conciliada(
        string caso, string restriccion, string columna)
    {
        Assert.NotEmpty(caso);
        var g = await GrafoDeIa.ObtenerAsync(db);

        var error = await RechazadoComoSuperusuario(Insertar("llamadas_ia", Conciliada(g, (columna, "null"))));

        Assert.Equal(ViolaCheck, error.SqlState);
        Assert.Equal(restriccion, error.ConstraintName);
    }

    [Theory]
    [InlineData("tokens_entrada", "ck_llamadas_ia_tokens_entrada_segun_estado", "5")]
    [InlineData("tokens_salida", "ck_llamadas_ia_tokens_salida_segun_estado", "5")]
    [InlineData("costo", "ck_llamadas_ia_costo_segun_estado", "5")]
    [InlineData("resultado", "ck_llamadas_ia_resultado_segun_estado", "'ok'")]
    [InlineData("conciliada_en", "ck_llamadas_ia_conciliada_en_segun_estado", "now()")]
    public async Task T25_una_llamada_Reservada_no_puede_traer_datos_de_conciliacion(string columna, string restriccion, string valor)
    {
        var g = await GrafoDeIa.ObtenerAsync(db);

        var error = await RechazadoComoSuperusuario(Insertar("llamadas_ia", Llamada(g, (columna, valor))));

        Assert.Equal(ViolaCheck, error.SqlState);
        Assert.Equal(restriccion, error.ConstraintName);
    }

    [Theory]
    [InlineData("tokens_entrada", "ck_llamadas_ia_tokens")]
    [InlineData("tokens_salida", "ck_llamadas_ia_tokens")]
    [InlineData("costo", "ck_llamadas_ia_costo")]
    public async Task T25_tokens_y_costo_negativos_son_rechazados(string columna, string restriccion)
    {
        var g = await GrafoDeIa.ObtenerAsync(db);

        var error = await RechazadoComoSuperusuario(Insertar("llamadas_ia", Conciliada(g, (columna, "-1"))));

        Assert.Equal(ViolaCheck, error.SqlState);
        Assert.Equal(restriccion, error.ConstraintName);
    }

    [Theory]
    [InlineData("tope", "0", "ck_presupuestos_ia_tope")]
    [InlineData("reservado", "-1", "ck_presupuestos_ia_reservado")]
    [InlineData("consumido", "-1", "ck_presupuestos_ia_consumido")]
    [InlineData("mes", "'2099-00'", "ck_presupuestos_ia_mes")]
    public async Task T25_los_checks_del_contador_rechazan_con_23514(string columna, string valor, string restriccion)
    {
        var error = await RechazadoComoSuperusuario(Insertar("presupuestos_ia", Contador((columna, valor))));

        Assert.Equal(ViolaCheck, error.SqlState);
        Assert.Equal(restriccion, error.ConstraintName);
    }

    // ---- Los índices únicos de R-01 a R-03 ----

    [Fact]
    public async Task T25_un_segundo_resumen_con_el_mismo_partido_y_huella_es_rechazado_con_23505()
    {
        var g = await GrafoDeIa.ObtenerAsync(db);

        var error = await RechazadoComoSuperusuario(
            Insertar("resumenes_partido", Resumen(g, ("huella", $"'{GrafoDeIa.HuellaValida}'"))));

        Assert.Equal(ViolaUnicidad, error.SqlState);
        Assert.Equal("ux_resumenes_partido_tenant_partido_huella", error.ConstraintName);
    }

    [Fact]
    public async Task T25_una_segunda_llamada_con_el_mismo_numero_para_la_misma_huella_es_rechazada_con_23505()
    {
        var g = await GrafoDeIa.ObtenerAsync(db);

        var error = await RechazadoComoSuperusuario(
            Insertar("llamadas_ia", Llamada(g, ("huella", $"'{GrafoDeIa.HuellaValida}'"), ("numero", "1"))));

        Assert.Equal(ViolaUnicidad, error.SqlState);
        Assert.Equal("ux_llamadas_ia_tenant_partido_huella_numero", error.ConstraintName);
    }

    [Fact]
    public async Task T25_un_segundo_contador_del_mismo_tenant_y_mes_es_rechazado_pero_otro_tenant_con_el_mismo_mes_no()
    {
        await GrafoDeIa.ObtenerAsync(db);

        var error = await RechazadoComoSuperusuario(Insertar("presupuestos_ia", Contador(("mes", $"'{GrafoDeIa.MesFijo}'"))));
        Assert.Equal(ViolaUnicidad, error.SqlState);
        Assert.Equal("ux_presupuestos_ia_tenant_mes", error.ConstraintName);

        // El índice incluye tenant_id (M6): la liga B ya tiene su contador del mismo mes y A tiene el suyo.
        await using var conexion = new NpgsqlConnection(db.CadenaSuperusuario);
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand(
            "select count(*) from goalflow.presupuestos_ia where mes = @mes", conexion);
        comando.Parameters.AddWithValue("mes", GrafoDeIa.MesFijo);
        Assert.Equal(2L, (long)(await comando.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task T25_las_filas_validas_por_defecto_de_cada_tabla_son_aceptadas()
    {
        var g = await GrafoDeIa.ObtenerAsync(db);
        await using var conexion = new NpgsqlConnection(db.CadenaSuperusuario);
        await conexion.OpenAsync();
        await using var transaccion = await conexion.BeginTransactionAsync();

        await Ejecutar(conexion, transaccion, Insertar("resumenes_partido", Resumen(g)));
        await Ejecutar(conexion, transaccion, Insertar("llamadas_ia", Llamada(g)));
        await Ejecutar(conexion, transaccion, Insertar("llamadas_ia", Conciliada(g, ("numero", "2"))));
        await Ejecutar(conexion, transaccion, Insertar("presupuestos_ia", Contador()));
    }

    // ---- Las claves foráneas compuestas (R-04, ADR-14 §2.11): una fila de A no puede referenciar una fila de B ----

    private async Task<PostgresException> RechazadoParaLigaA(string sql)
    {
        await using var conexion = new NpgsqlConnection(db.CadenaApp);
        await conexion.OpenAsync();
        await using var transaccion = await conexion.BeginTransactionAsync();
        await Ejecutar(conexion, transaccion, $"select set_config('app.tenant_id', '{LigaA.Value}', true)");
        return await Assert.ThrowsAsync<PostgresException>(() => Ejecutar(conexion, transaccion, sql));
    }

    [Theory]
    [InlineData("resumen → partido de B", "fk_resumenes_partido_partidos")]
    [InlineData("resumen → llamada de B", "fk_resumenes_partido_llamadas")]
    [InlineData("llamada → partido de B", "fk_llamadas_ia_partidos")]
    public async Task T25_cada_clave_foranea_nueva_rechaza_una_referencia_a_B_con_23503(string clave, string restriccion)
    {
        var g = await GrafoDeIa.ObtenerAsync(db);
        var sql = clave switch
        {
            "resumen → partido de B" => Insertar("resumenes_partido", Resumen(g, ("partido_id", $"'{g.Planillas.PartidoB}'"))),
            "resumen → llamada de B" => Insertar("resumenes_partido", Resumen(g,
                ("origen", "'Modelo'"), ("modelo", "'claude-haiku-5-5'"), ("version_prompt", "'a01-v1'"), ("llamada_id", $"'{g.LlamadaB}'"))),
            "llamada → partido de B" => Insertar("llamadas_ia", Llamada(g, ("partido_id", $"'{g.Planillas.PartidoB}'"))),
            _ => throw new InvalidOperationException(clave),
        };

        var error = await RechazadoParaLigaA(sql);

        Assert.Equal(ViolaClaveForanea, error.SqlState);
        Assert.Equal(restriccion, error.ConstraintName);
    }

    [Fact]
    public async Task T25_las_tres_claves_foraneas_y_las_de_tenant_usan_ON_DELETE_RESTRICT()
    {
        await using var conexion = new NpgsqlConnection(db.CadenaSuperusuario);
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand("""
            select conname, confdeltype::text
            from pg_constraint c join pg_class t on t.oid = c.conrelid join pg_namespace n on n.oid = t.relnamespace
            where n.nspname = 'goalflow' and c.contype = 'f' and t.relname = any(@t)
            """, conexion);
        comando.Parameters.AddWithValue("t", Tablas);
        await using var lector = await comando.ExecuteReaderAsync();
        var claves = new Dictionary<string, string>();
        while (await lector.ReadAsync()) claves[lector.GetString(0)] = lector.GetString(1);

        Assert.True(claves.Count >= 3, "Faltan claves foráneas");
        Assert.Contains("fk_resumenes_partido_partidos", claves.Keys);
        Assert.Contains("fk_resumenes_partido_llamadas", claves.Keys);
        Assert.Contains("fk_llamadas_ia_partidos", claves.Keys);
        Assert.All(claves, c => Assert.Equal("r", c.Value));
    }

    // ---- Privilegios exactos, RLS y una política por tabla (R-04) ----

    [Theory]
    [InlineData("resumenes_partido", "SELECT,INSERT")]
    [InlineData("llamadas_ia", "SELECT,INSERT,UPDATE")]
    [InlineData("presupuestos_ia", "SELECT,INSERT,UPDATE")]
    public async Task T25_goalflow_app_tiene_exactamente_los_privilegios_de_R_04(string tabla, string esperados)
    {
        await using var conexion = new NpgsqlConnection(db.CadenaSuperusuario);
        await conexion.OpenAsync();
        var concedidos = new List<string>();
        foreach (var privilegio in new[] { "SELECT", "INSERT", "UPDATE", "DELETE", "TRUNCATE", "REFERENCES", "TRIGGER" })
        {
            await using var comando = new NpgsqlCommand(
                "select has_table_privilege('goalflow_app', @tabla, @privilegio)", conexion);
            comando.Parameters.AddWithValue("tabla", $"goalflow.{tabla}");
            comando.Parameters.AddWithValue("privilegio", privilegio);
            if ((bool)(await comando.ExecuteScalarAsync())!) concedidos.Add(privilegio);
        }

        Assert.Equal(esperados.Split(','), concedidos);
    }

    [Theory]
    [InlineData("resumenes_partido")]
    [InlineData("llamadas_ia")]
    [InlineData("presupuestos_ia")]
    public async Task T25_cada_tabla_tiene_RLS_activa_y_exactamente_una_politica_de_tenant(string tabla)
    {
        await using var conexion = new NpgsqlConnection(db.CadenaSuperusuario);
        await conexion.OpenAsync();
        await using var rls = new NpgsqlCommand(
            "select c.relrowsecurity from pg_class c join pg_namespace n on n.oid = c.relnamespace where n.nspname = 'goalflow' and c.relname = @t",
            conexion);
        rls.Parameters.AddWithValue("t", tabla);
        Assert.True((bool)(await rls.ExecuteScalarAsync())!);

        await using var politicas = new NpgsqlCommand(
            "select policyname, qual, with_check from pg_policies where schemaname = 'goalflow' and tablename = @t", conexion);
        politicas.Parameters.AddWithValue("t", tabla);
        await using var lector = await politicas.ExecuteReaderAsync();
        Assert.True(await lector.ReadAsync(), "No hay política");
        Assert.Equal($"{tabla}_tenant", lector.GetString(0));
        Assert.Contains("app.tenant_id", lector.GetString(1));
        Assert.Contains("app.tenant_id", lector.GetString(2));
        Assert.False(await lector.ReadAsync(), "Hay más de una política");
    }

    [Fact]
    public async Task T25_goalflow_app_no_es_dueno_de_ninguna_de_las_tres_tablas()
    {
        await using var conexion = new NpgsqlConnection(db.CadenaSuperusuario);
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand("""
            select count(*) from pg_class c join pg_namespace n on n.oid = c.relnamespace
            where n.nspname = 'goalflow' and c.relname = any(@t) and pg_get_userbyid(c.relowner) = 'goalflow_app'
            """, conexion);
        comando.Parameters.AddWithValue("t", Tablas);

        Assert.Equal(0L, (long)(await comando.ExecuteScalarAsync())!);
    }
}
