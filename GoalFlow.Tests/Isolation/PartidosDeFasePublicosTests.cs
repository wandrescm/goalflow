using System.Net;
using static GoalFlow.Tests.Isolation.EntornoDeLecturasPublicas;

namespace GoalFlow.Tests.Isolation;

/// <summary>
/// Spec 011 commit 3: <c>GET /{slug}/torneos/{codigo}/fases/{orden}/partidos</c> (R-03 a R-07) por la API real contra PostgreSQL 17
/// como goalflow_app. El cuerpo esperado se arma aquí, literal, desde la Spec.
/// </summary>
[Collection(ColeccionDePlanilla.Nombre)]
public class PartidosDeFasePublicosTests(BaseDeAislamiento db)
{
    private static object Eq(string codigo, string nombre) => new { codigo, nombre };

    private static object? Marc((int Local, int Visitante)? m) => m is { } v ? new { local = v.Local, visitante = v.Visitante } : null;

    private static object Partido(
        int jornada, string? hora, (string Codigo, string Nombre) local, (string Codigo, string Nombre) visitante, string estado,
        (int, int)? marcador = null, (int, int)? oficial = null, string? motivo = null) => new
    {
        jornada,
        programadoPara = hora,
        local = Eq(local.Codigo, local.Nombre),
        visitante = Eq(visitante.Codigo, visitante.Nombre),
        estado,
        marcador = Marc(marcador),
        oficial = Marc(oficial),
        motivo,
    };

    private static (string, string) E(int i) => EquiposT02[i - 1];

    /// <summary>La lista de T-02 en el orden de R-06 (jornada, hora con null al final, código local, código visitante, ordinales).</summary>
    private static object ListaEsperadaT02() => new
    {
        torneo = new { codigo = TorneoT02, nombre = "Torneo Dos" },
        fase = new { orden = 1 },
        partidos = new[]
        {
            Partido(1, "2026-11-21T01:00:00Z", E(1), E(2), "Jugado", marcador: (1, 1)),          // corregido de 2–0 (v1) a 1–1 (v2)
            Partido(1, null, E(3), E(4), "Jugado", marcador: (3, 1)),
            Partido(1, null, E(7), E(8), "Cancelado"),                                           // «E7» antes que «e5»: orden ordinal
            Partido(1, null, E(5), E(6), "Programado"),
            Partido(2, "2026-11-22T14:00:00Z", E(5), E(7), "Adjudicado", marcador: (2, 1), oficial: (3, 0), motivo: "Sancion"),
            Partido(2, "2026-11-22T15:30:00Z", E(1), E(3), "Suspendido", marcador: (1, 0), motivo: "Disturbios"),
            Partido(2, null, E(2), E(4), "Adjudicado", oficial: (3, 0), motivo: "WalkOver"),
            Partido(2, null, E(6), E(8), "Cancelado"),                                           // C-32: Cancelado sobre una planilla retirada
        },
    };

    [Fact] // T-02
    public async Task T02_la_lista_de_una_fase_devuelve_el_cuerpo_exacto_con_los_cinco_estados_y_solo_la_planilla_vigente()
    {
        await using var e = new EntornoDeLecturasPublicas(db);
        var liga = await e.CrearLiga();
        await e.CargarT02(liga);

        IgualesLiteralmente(ListaEsperadaT02(), await e.Texto(Partidos(liga, TorneoT02)));
    }

    [Fact] // T-02: la hora programada se escribe literalmente en UTC con Z (R-07)
    public async Task T02_programadoPara_termina_en_Z_y_nunca_se_convierte_a_hora_local()
    {
        await using var e = new EntornoDeLecturasPublicas(db);
        var liga = await e.CrearLiga();
        await e.CargarT02(liga);

        var texto = await e.Texto(Partidos(liga, TorneoT02));

        Assert.Contains("\"programadoPara\":\"2026-11-21T01:00:00Z\"", texto, StringComparison.Ordinal);
        Assert.Contains("\"programadoPara\":null", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("-05:00", texto, StringComparison.Ordinal);
    }

    // ---- T-13 (lista) ----

    [Theory] // T-13 (a): orden mal formado en un slug existente: 400, cero comandos desde el inicio de la petición, el resolutor sin llamadas
    [MemberData(nameof(Ordenes))]
    public async Task T13_un_orden_mal_formado_da_400_sin_comandos_de_base_ni_resolucion_del_slug(string orden)
    {
        await using var e = new EntornoDeLecturasPublicas(db);

        var (estado, _, comandos, resoluciones) = await e.Medir($"/liga-a/torneos/{TorneoT02}/fases/{orden}/partidos");

        Assert.Equal(HttpStatusCode.BadRequest, estado);
        Assert.Equal((0, 0), (comandos, resoluciones));
    }

    [Theory] // T-13 (a): código de torneo mal formado
    [MemberData(nameof(Codigos))]
    public async Task T13_un_codigo_de_torneo_mal_formado_da_400_sin_comandos_de_base_ni_resolucion_del_slug(string codigo)
    {
        await using var e = new EntornoDeLecturasPublicas(db);

        var (estado, _, comandos, resoluciones) = await e.Medir($"/liga-a/torneos/{codigo}/fases/1/partidos");

        Assert.Equal(HttpStatusCode.BadRequest, estado);
        Assert.Equal((0, 0), (comandos, resoluciones));
    }

    public static IEnumerable<object[]> Ordenes() => OrdenesMalFormados.Select(o => new object[] { o });

    public static IEnumerable<object[]> Codigos() => CodigosMalFormados.Select(c => new object[] { c });

    [Fact] // T-13 (b): torneo, fase o liga inexistentes y códigos que solo difieren en mayúsculas dan 404, con el cuerpo de cualquier 404
    public async Task T13_lo_inexistente_y_los_codigos_que_solo_difieren_en_mayusculas_dan_404()
    {
        await using var e = new EntornoDeLecturasPublicas(db);
        var liga = await e.CrearLiga();
        await e.CargarT02(liga);
        var (_, deRutaDesconocida) = await EntornoDeEstadisticas.Leer(await e.Get($"/{liga}/ruta-que-no-existe/x"));

        foreach (var ruta in new[]
        {
            Partidos(liga, "NO-EXISTE"), Partidos(liga, TorneoT02, 7), Partidos(liga, TorneoT02.ToLowerInvariant()), Partidos("liga-que-no-existe", TorneoT02),
        })
        {
            var (estado, texto) = await EntornoDeEstadisticas.Leer(await e.Get(ruta));
            Assert.True(estado == HttpStatusCode.NotFound, $"{ruta} debía dar 404 y dio {estado}");
            Assert.Equal(deRutaDesconocida, texto);
        }
    }

    [Fact] // T-13 (c): orden=01 responde exactamente como 1 (C-25)
    public async Task T13_el_orden_01_responde_igual_que_el_1()
    {
        await using var e = new EntornoDeLecturasPublicas(db);
        var liga = await e.CrearLiga();
        await e.CargarT02(liga);

        var con1 = await e.Texto(Partidos(liga, TorneoT02, 1));
        var con01 = await e.Texto($"/{liga}/torneos/{TorneoT02}/fases/01/partidos");

        Assert.Equal(con1, con01);
    }

    [Fact] // T-13 (d) y R-23: un Authorization inválido no cambia el cuerpo ni el número de comandos
    public async Task T13_un_Authorization_invalido_da_el_mismo_cuerpo_y_los_mismos_comandos_que_sin_el()
    {
        await using var e = new EntornoDeLecturasPublicas(db);
        var liga = await e.CrearLiga();
        await e.CargarT02(liga);
        var ruta = Partidos(liga, TorneoT02);

        var sin = await e.Medir(ruta);
        var con = await e.Medir(ruta, "Bearer x");

        Assert.Equal(HttpStatusCode.OK, sin.Estado);
        Assert.Equal((sin.Estado, sin.Texto, sin.Comandos), (con.Estado, con.Texto, con.Comandos));
    }

    // ---- T-12 y T-15 (lista) ----

    [Fact] // T-12 (lista)
    public async Task T12_la_lista_no_filtra_ningun_identificador_ni_miembro_interno()
    {
        await using var e = new EntornoDeLecturasPublicas(db);
        var liga = await e.CrearLiga();
        await e.CargarT02(liga);

        SinFugas(await e.Texto(Partidos(liga, TorneoT02)));
    }

    [Fact] // T-15 (lista): el fixture de T-02 insertado en orden inverso da el mismo cuerpo, byte a byte
    public async Task T15_la_lista_no_depende_del_orden_de_insercion()
    {
        await using var e = new EntornoDeLecturasPublicas(db);
        var (directa, inversa) = (await e.CrearLiga(), await e.CrearLiga());
        await e.CargarT02(directa);
        await e.CargarT02(inversa, inverso: true);

        Assert.Equal(await e.Texto(Partidos(directa, TorneoT02)), await e.Texto(Partidos(inversa, TorneoT02)));
    }
}
