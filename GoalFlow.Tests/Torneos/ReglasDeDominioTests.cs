using GoalFlow.Domain.Tenancy;
using GoalFlow.Domain.Torneos;

namespace GoalFlow.Tests.Torneos;

/// <summary>Spec 005 T-01 (R-12): cada entrada inválida lanza su excepción específica; los límites válidos se aceptan.</summary>
public class ReglasDeDominioTests
{
    private static readonly TenantId Liga = new(Guid.NewGuid());
    private static readonly Guid Torneo = Guid.NewGuid();

    public enum Entidad
    {
        Equipo,
        Jugador,
        Torneo,
    }

    private static object Crear(Entidad entidad, string codigo, string nombre) => entidad switch
    {
        Entidad.Equipo => new Equipo(Guid.NewGuid(), Liga, codigo, nombre),
        Entidad.Jugador => new Jugador(Guid.NewGuid(), Liga, codigo, nombre),
        _ => new GoalFlow.Domain.Torneos.Torneo(Guid.NewGuid(), Liga, codigo, nombre),
    };

    private static void ExigirRazon(string razon, Action accion) =>
        Assert.Equal(razon, Assert.Throws<ReglaDeDominioException>(accion).Razon);

    public static TheoryData<Entidad, string> CodigosInvalidos()
    {
        var datos = new TheoryData<Entidad, string>();
        foreach (var entidad in Enum.GetValues<Entidad>())
        foreach (var codigo in new[] { "", " ", "EQ 01", "EQ/01", "EQ.01", "ÑU-01", new string('A', 33) })
            datos.Add(entidad, codigo);
        return datos;
    }

    [Theory]
    [MemberData(nameof(CodigosInvalidos))]
    public void Un_codigo_vacio_o_invalido_lanza_la_excepcion_especifica(Entidad entidad, string codigo) =>
        ExigirRazon(RazonesDeDominio.CodigoInvalido, () => Crear(entidad, codigo, "Nombre Uno"));

    public static TheoryData<Entidad, string> NombresInvalidos()
    {
        var datos = new TheoryData<Entidad, string>();
        foreach (var entidad in Enum.GetValues<Entidad>())
        foreach (var nombre in new[] { "", "   ", new string('N', 81), "Nombre\u0007Uno", "Nombre\nUno" })
            datos.Add(entidad, nombre);
        return datos;
    }

    [Theory]
    [MemberData(nameof(NombresInvalidos))]
    public void Un_nombre_vacio_demasiado_largo_o_con_control_lanza_la_excepcion_especifica(Entidad entidad, string nombre) =>
        ExigirRazon(RazonesDeDominio.NombreInvalido, () => Crear(entidad, "EQ-01", nombre));

    [Theory]
    [InlineData(Entidad.Equipo)]
    [InlineData(Entidad.Jugador)]
    [InlineData(Entidad.Torneo)]
    public void Los_limites_validos_se_aceptan_y_el_nombre_se_recorta(Entidad entidad)
    {
        var codigoLargo = new string('A', 32);
        var nombreLargo = new string('N', 80);

        Assert.NotNull(Crear(entidad, codigoLargo, nombreLargo));
        Assert.NotNull(Crear(entidad, "a_B-9", "N"));

        var recortado = Crear(entidad, "EQ-01", "  Equipo Uno  ");
        var nombre = recortado switch
        {
            Equipo e => e.Nombre,
            Jugador j => j.Nombre,
            GoalFlow.Domain.Torneos.Torneo t => t.Nombre,
            _ => throw new InvalidOperationException(),
        };
        Assert.Equal("Equipo Uno", nombre);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Una_jornada_menor_que_1_lanza_la_excepcion_especifica(int jornada)
    {
        var (local, visitante) = (Guid.NewGuid(), Guid.NewGuid());

        ExigirRazon(RazonesDeDominio.JornadaInvalida, () =>
            new Partido(Guid.NewGuid(), Liga, Torneo, Guid.NewGuid(), jornada, local, visitante, null));

        var partido = new Partido(Guid.NewGuid(), Liga, Torneo, Guid.NewGuid(), 1, local, visitante, null);
        ExigirRazon(RazonesDeDominio.JornadaInvalida, () => partido.Reprogramar(jornada, null));
    }

    [Fact]
    public void Una_jornada_1_es_valida()
    {
        var partido = new Partido(Guid.NewGuid(), Liga, Torneo, Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), null);

        Assert.Equal(1, partido.Jornada);
    }

    [Fact]
    public void Un_partido_con_el_mismo_local_y_visitante_lanza_la_excepcion_especifica()
    {
        var mismo = Guid.NewGuid();

        ExigirRazon(RazonesDeDominio.LocalIgualAVisitante, () =>
            new Partido(Guid.NewGuid(), Liga, Torneo, Guid.NewGuid(), 1, mismo, mismo, null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(-1)]
    public void Unas_vueltas_distintas_de_1_o_2_lanzan_la_excepcion_especifica(int vueltas)
    {
        ExigirRazon(RazonesDeDominio.VueltasInvalidas, () =>
            new Fase(Guid.NewGuid(), Liga, Torneo, 1, TipoDeFase.TodosContraTodos, vueltas));

        var fase = new Fase(Guid.NewGuid(), Liga, Torneo, 1, TipoDeFase.TodosContraTodos, 1);
        ExigirRazon(RazonesDeDominio.VueltasInvalidas, () => fase.Configurar(TipoDeFase.TodosContraTodos, vueltas));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Unas_vueltas_1_o_2_son_validas(int vueltas) =>
        Assert.Equal(vueltas, new Fase(Guid.NewGuid(), Liga, Torneo, 1, TipoDeFase.TodosContraTodos, vueltas).Vueltas);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Un_orden_menor_que_1_lanza_la_excepcion_especifica(int orden) =>
        ExigirRazon(RazonesDeDominio.OrdenInvalido, () =>
            new Fase(Guid.NewGuid(), Liga, Torneo, orden, TipoDeFase.TodosContraTodos, 1));

    [Fact]
    public void Un_orden_1_es_valido() =>
        Assert.Equal(1, new Fase(Guid.NewGuid(), Liga, Torneo, 1, TipoDeFase.TodosContraTodos, 1).Orden);

    [Theory]
    [InlineData(Entidad.Equipo)]
    [InlineData(Entidad.Jugador)]
    [InlineData(Entidad.Torneo)]
    public void Renombrar_con_un_nombre_invalido_lanza_la_excepcion_especifica(Entidad entidad)
    {
        var creado = Crear(entidad, "EQ-01", "Nombre Uno");

        ExigirRazon(RazonesDeDominio.NombreInvalido, () =>
        {
            switch (creado)
            {
                case Equipo e: e.Renombrar(" "); break;
                case Jugador j: j.Renombrar(" "); break;
                case GoalFlow.Domain.Torneos.Torneo t: t.Renombrar(" "); break;
            }
        });
    }
}
