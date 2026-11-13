using GoalFlow.Application.IA;
using GoalFlow.Application.IA.Verificacion;
using GoalFlow.Application.Torneos.Contracts;
using GoalFlow.Domain.IA;
using GoalFlow.Domain.Tenancy;

namespace GoalFlow.Tests.IA.Verificacion;

/// <summary>Spec 008 T-13 (R-17, R-18): la publicación se deriva en cada lectura, con el verificador vigente. Filas en memoria.</summary>
public class SelectorDePublicacionTests
{
    private static readonly TenantId Tenant = new(Guid.Parse("00000000-0000-0000-0000-00000000000a"));

    private static ResumenPartido Fila(EntradaCanonica entrada, string texto, EstadoDeResumen estado = EstadoDeResumen.PorVerificar,
        OrigenDeResumen origen = OrigenDeResumen.Modelo, int version = 1) =>
        new(Guid.NewGuid(), Tenant, Guid.Empty, Guid.Empty, version, entrada.Huella(), origen, estado,
            estado == EstadoDeResumen.Borrador ? MotivoDeBorrador.LimiteDeLlamadas : null, texto,
            origen == OrigenDeResumen.Modelo ? "modelo-de-prueba" : null, origen == OrigenDeResumen.Modelo ? "a01-v1" : null, null);

    [Fact]
    public void T13a_un_PorVerificar_con_la_huella_vigente_y_un_texto_aprobado_se_devuelve()
    {
        var entrada = CorpusV1.Entrada("F1");
        var fila = Fila(entrada, CorpusV1.Caso("C1").Texto);

        Assert.Same(fila, SelectorDePublicacion.Seleccionar([fila], entrada));
    }

    [Fact]
    public void T13b_la_misma_fila_con_otra_huella_vigente_no_se_devuelve()
    {
        var fila = Fila(CorpusV1.Entrada("F1"), CorpusV1.Caso("C1").Texto);

        Assert.Null(SelectorDePublicacion.Seleccionar([fila], CorpusV1.Entrada("F2")));
    }

    [Fact]
    public void T13c_un_Borrador_con_la_huella_vigente_nunca_se_devuelve()
    {
        var entrada = CorpusV1.Entrada("F1");
        var borrador = Fila(entrada, PlantillasDeResumen.BorradorExtendido(entrada, 4000), EstadoDeResumen.Borrador, OrigenDeResumen.Plantilla);

        Assert.True(VerificadorDeResumen.Verificar(borrador.Texto, entrada).Aprobado); // aprobado por el verificador, y aun así no se publica
        Assert.Null(SelectorDePublicacion.Seleccionar([borrador], entrada));
    }

    [Fact]
    public void T13d_un_PorVerificar_con_la_huella_vigente_cuyo_texto_el_verificador_rechaza_no_se_devuelve()
    {
        var entrada = CorpusV1.Entrada("F1");
        var fila = Fila(entrada, CorpusV1.Caso("M6").Texto); // insertada directamente: nunca pasó por el consumidor

        Assert.Null(SelectorDePublicacion.Seleccionar([fila], entrada));
    }

    [Fact]
    public void T13e_una_Plantilla_PorVerificar_se_devuelve()
    {
        var entrada = EntradaCanonica.Construir(DatosDePrueba.Vacio());
        var fila = Fila(entrada, PlantillasDeResumen.Plantilla(entrada), origen: OrigenDeResumen.Plantilla);

        Assert.Same(fila, SelectorDePublicacion.Seleccionar([fila], entrada));
    }

    [Fact]
    public void T13f_la_publicacion_sigue_al_contenido_y_no_a_la_version_en_la_secuencia_de_correcciones_revertidas()
    {
        EntradaCanonica Con(string goleador) => EntradaCanonica.Construir(DatosDePrueba.Vacio() with
        {
            Marcador = new MarcadorDelPartido(1, 0),
            Goles = [new GolDelPartido(LadoDelEquipo.Local, goleador, "J-1", 1)],
        });

        var (uno, dos) = (Con("Jugador Uno"), Con("Jugador Dos"));
        var filaV1 = Fila(uno, PlantillasDeResumen.BorradorExtendido(uno, 4000), version: 1);
        var filaV2 = Fila(dos, PlantillasDeResumen.BorradorExtendido(dos, 4000), version: 2);
        var guardadas = new[] { filaV1, filaV2 };

        // v3 restaura el contenido de la v1: se publica la fila de la v1, sin fila nueva.
        Assert.Same(filaV1, SelectorDePublicacion.Seleccionar(guardadas, Con("Jugador Uno")));
        // v4 vuelve al contenido de la v2: se publica la fila de la v2.
        Assert.Same(filaV2, SelectorDePublicacion.Seleccionar(guardadas, Con("Jugador Dos")));
        // Un contenido sin fila: nada.
        Assert.Null(SelectorDePublicacion.Seleccionar(guardadas, CorpusV1.Entrada("F1")));
    }
}
