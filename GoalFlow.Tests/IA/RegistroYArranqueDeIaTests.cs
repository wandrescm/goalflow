using System.Net;
using GoalFlow.Application.IA;
using GoalFlow.Application.Torneos.Contracts;
using GoalFlow.Tests.Isolation;
using GoalFlow.Tests.Mensajeria;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static GoalFlow.Tests.Isolation.BaseDeAislamiento;

namespace GoalFlow.Tests.IA;

/// <summary>Spec 007 T-23 (R-27) y T-28 (R-06, R-07): los registros y el arranque del módulo.</summary>
[Collection(ColeccionDeIa.Nombre)]
public class RegistroYArranqueDeIaTests(BaseDeAislamiento db)
{
    private const string Consumidor = "ia.resumen-partido";

    // ---- T-23 (R-27) ----

    [Fact]
    public async Task T23_ningun_nombre_texto_clave_ni_mensaje_de_excepcion_llega_a_un_registro_en_ningun_nivel()
    {
        var registros = new CapturaDeRegistros();
        await using var host = AnfitrionDeIa.Construir(
            db, c => c["IA:ClaveApi"] = "clave-MARCA-K-9", registros: registros);
        var sufijo = " MARCA-N-7";
        var p = await PartidoSembrado.CrearAsync(db, LigaA, sufijo);
        await p.GuardarAsync(PlanillaSembrada.ConGoleador());
        var textoConMarca = "MARCA-T-5 " + ControlDeGenerador.TextoValido;

        // 1) Un éxito con exceso de costo: llegan advertencias y datos de uso.
        host.Control.AlGenerar = _ => Task.FromResult(
            new RespuestaGenerada(ControlDeGenerador.Json(textoConMarca), "end_turn", 20_000, 400));
        await host.ProcesarAsync(p, 1);

        // 2) Una llamada incierta cuya excepción trae un mensaje con marca, y un error de respuesta, y una salida inválida.
        var p2 = await PartidoSembrado.CrearAsync(db, LigaA, sufijo);
        await p2.GuardarAsync(PlanillaSembrada.ConGoleador("Jugador Dos"));
        foreach (Func<Exception> falla in new Func<Exception>[]
        {
            () => new ResultadoInciertoException("MARCA-E-3"),
            () => new RespuestaConErrorException(500, null, "MARCA-E-4"),
            () => new SalidaInvalidaException(new UsoReportado(10, 10), "MARCA-E-5"),
        })
        {
            host.Control.AlGenerar = _ => throw falla();
            await Assert.ThrowsAsync<FalloReintentableException>(() => host.ProcesarAsync(p2, 1));
        }

        var todo = string.Join('\n', registros.Entradas.Select(e => e.Texto));
        Assert.DoesNotContain("MARCA-N-7", todo); // nombres
        Assert.DoesNotContain("MARCA-T-5", todo); // texto generado
        Assert.DoesNotContain("MARCA-K-9", todo); // clave
        Assert.DoesNotContain("MARCA-E-", todo); // mensajes de excepción
        Assert.DoesNotContain("Jugador Uno", todo);
        Assert.DoesNotContain("Equipo Uno", todo);
        Assert.NotEmpty(registros.Entradas);

        // Lo que sí debe aparecer: el código de resultado y los tokens.
        Assert.Contains("exito_exceso", todo);
        Assert.Contains("20000", todo);
        Assert.Contains(registros.Entradas, e => e.Nivel == LogLevel.Warning);
    }

    // ---- T-28 (R-06): cada opción inválida impide arrancar con la IA habilitada ----

    [Theory]
    [InlineData("IA:ClaveApi", null)]
    [InlineData("IA:ClaveApi", "")]
    [InlineData("IA:Modelo", "")]
    [InlineData("IA:PrecioEntradaMicroUsdPorMTok", "0")]
    [InlineData("IA:PrecioSalidaMicroUsdPorMTok", "0")]
    [InlineData("IA:TopeMensualPorLigaMicroUsd", "0")]
    [InlineData("IA:TopeMensualPorLigaMicroUsd", "3000001")] // mayor que el global
    [InlineData("IA:MaximoDeLlamadasPorHuella", "0")]
    [InlineData("IA:MaxTokensDeSalida", "0")]
    [InlineData("IA:MaximoDeBytesDeEntrada", "0")]
    [InlineData("IA:MaximoDeBytesDeEntrada", "65537")]
    [InlineData("IA:MaximoDeTokensDeEntrada", "0")]
    [InlineData("IA:MaximoDeTokensDeEntrada", "50001")]
    [InlineData("IA:MargenDeEntradaPorcentaje", "-1")]
    [InlineData("IA:MargenDeEntradaTokens", "-1")]
    [InlineData("IA:LongitudMaximaDeBorrador", "399")]
    [InlineData("IA:LongitudMaximaDeBorrador", "4001")]
    [InlineData("IA:Razonamiento", "Siempre")]
    [InlineData("IA:TimeoutDeLlamada", "00:00:10")] // no es menor que el timeout del consumidor (5 s en estas pruebas)
    public async Task T28_cada_opcion_invalida_con_la_IA_habilitada_impide_arrancar(string clave, string? valor)
    {
        await using var host = AnfitrionDeIa.Construir(db, c => c[clave] = valor);

        await Assert.ThrowsAnyAsync<Exception>(host.IniciarAsync);
    }

    [Fact]
    public async Task T28_las_mismas_opciones_invalidas_no_importan_con_la_IA_apagada_y_las_validas_arrancan()
    {
        await using (var apagada = AnfitrionDeIa.Construir(db, c =>
        {
            c["IA:Habilitada"] = "false";
            c["IA:ClaveApi"] = null;
            c["IA:PrecioEntradaMicroUsdPorMTok"] = "0";
            c["IA:MaximoDeBytesDeEntrada"] = "0";
        }, conGenerador: false))
        {
            await apagada.IniciarAsync();
        }

        await using var valida = AnfitrionDeIa.Construir(db);
        await valida.IniciarAsync();
    }

    // ---- T-28 (R-07): con la IA apagada el consumidor no existe ----

    [Fact]
    public async Task T28_con_la_IA_apagada_el_consumidor_no_esta_registrado_y_guardar_una_planilla_no_escribe_entrega_de_A01()
    {
        await using var e = new EntornoDePlanilla(db);
        var p = await e.Preparar();
        var ruta = EntornoDePlanilla.RutaDelPrimerPartido(p);

        var guardado = await e.Guardar(ruta, EntornoDePlanilla.Jugado3a1(p, 0));

        Assert.Equal(HttpStatusCode.OK, guardado.StatusCode);
        Assert.Equal(0, await e.Contar($"select count(*) from goalflow.entregas_eventos where consumidor = '{Consumidor}'"));
        using (var scope = e.Services.CreateScope())
        {
            Assert.Null(scope.ServiceProvider.GetService<ConsumidorDeResumenDePartido>());
            Assert.Null(scope.ServiceProvider.GetService<IGeneradorDeTexto>()); // y nada puede llamar al proveedor (R-07)
        }

        var registros = e.Services.GetServices<GoalFlow.Infrastructure.Mensajeria.RegistroDeConsumidor>();
        Assert.DoesNotContain(registros, r => r.Nombre == Consumidor);
    }

    [Fact]
    public async Task T28_con_la_IA_encendida_guardar_una_planilla_escribe_la_entrega_de_A01()
    {
        await using var e = new EntornoDePlanilla(db, ajustarAnfitrion: b =>
        {
            b.UseSetting("IA:Habilitada", "true");
            b.UseSetting("IA:ClaveApi", "clave-falsa-de-prueba");
            b.ConfigureTestServices(s =>
            {
                s.AddSingleton<ControlDeGenerador>();
                s.AddScoped<IGeneradorDeTexto, GeneradorFalso>();
            });
        });
        var p = await e.Preparar();

        var guardado = await e.Guardar(EntornoDePlanilla.RutaDelPrimerPartido(p), EntornoDePlanilla.Jugado3a1(p, 0));

        Assert.Equal(HttpStatusCode.OK, guardado.StatusCode);
        Assert.Equal(1, await e.Contar($"""
            select count(*) from goalflow.entregas_eventos en
            where en.consumidor = '{Consumidor}' and en.agregado_id = ({e.FiltroDePartido(p, 1, 2)})
            """));
    }

    // ---- T-28: las entregas que ya existen sobreviven a apagar y encender la IA ----

    [Fact]
    public async Task T28_apagar_la_IA_no_pierde_ni_falla_ni_gasta_intentos_de_las_entregas_existentes_y_al_encenderla_se_procesan()
    {
        var p1 = await PartidoSembrado.CrearAsync(db, LigaA);
        var p2 = await PartidoSembrado.CrearAsync(db, LigaA);
        await p1.GuardarAsync(PlanillaSembrada.ConGoleador());
        await p2.GuardarAsync(PlanillaSembrada.ConGoleador("Jugador Dos"));
        var (pendiente, enProceso, llamada) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var huellaVieja = new string('d', 64);
        var mesViejo = "2099-05";
        await db.EjecutarComoSuperusuario($"""
            insert into goalflow.entregas_eventos (id, tenant_id, evento_id, tipo_de_evento, agregado_id, version_del_agregado, consumidor, estado, intentos)
            values ('{pendiente}', '{LigaA.Value}', '{Guid.NewGuid()}', 'torneos.partido-guardado', '{p1.Id}', 1, '{Consumidor}', 'Pendiente', 0);
            insert into goalflow.entregas_eventos (id, tenant_id, evento_id, tipo_de_evento, agregado_id, version_del_agregado, consumidor, estado, intentos, token_del_lease, lease_vence_en)
            values ('{enProceso}', '{LigaA.Value}', '{Guid.NewGuid()}', 'torneos.partido-guardado', '{p2.Id}', 1, '{Consumidor}', 'EnProceso', 1, '{Guid.NewGuid()}', now() - interval '1 hour');
            insert into goalflow.presupuestos_ia (id, tenant_id, mes, tope, reservado, consumido) values ('{Guid.NewGuid()}', '{LigaA.Value}', '{mesViejo}', 1500000, 525, 0);
            insert into goalflow.llamadas_ia (id, tenant_id, torneo_id, partido_id, version, huella, numero, intento_entrega, mes, reserva, estado)
            values ('{llamada}', '{LigaA.Value}', '{p2.TorneoId}', '{p2.Id}', 1, '{huellaVieja}', 1, 1, '{mesViejo}', 525, 'Reservada');
            """);
        const string foto = """
            select id::text, estado, intentos, token_del_lease::text, lease_vence_en::text, ultimo_error
            from goalflow.entregas_eventos where consumidor = 'ia.resumen-partido' and agregado_id = any(@ids) order by id
            """;
        async Task<string> Foto(AnfitrionDeIa a) => string.Join(
            '|', (await a.Filas(foto.Replace("any(@ids)", $"any(array['{p1.Id}', '{p2.Id}']::uuid[])")))
                .Select(f => string.Join(',', f.Select(v => v?.ToString()))));

        // Con la IA apagada y el ciclo corriendo: ni se reclaman, ni fallan, ni gastan intentos.
        string antes;
        await using (var apagada = AnfitrionDeIa.Construir(db, c =>
        {
            c["Mensajeria:CicloHabilitado"] = "true";
            c["IA:Habilitada"] = "false";
            c["IA:ClaveApi"] = null;
        }, conGenerador: false))
        {
            antes = await Foto(apagada);
            await apagada.IniciarAsync();
            await Task.Delay(TimeSpan.FromSeconds(3.5)); // al menos dos pasadas del temporizador de 1 s
            Assert.Equal(antes, await Foto(apagada));
            Assert.DoesNotContain("Fallida", await Foto(apagada));
            Assert.Equal(
                (525L, 0L),
                ((long)(await apagada.Filas($"select reservado, consumido from goalflow.presupuestos_ia where tenant_id = '{LigaA.Value}' and mes = '{mesViejo}'"))[0][0]!, 0L));
            Assert.Equal("Reservada", (string)(await apagada.Filas($"select estado from goalflow.llamadas_ia where id = '{llamada}'"))[0][0]!);
        }

        // Con la IA encendida: ambas se procesan.
        await using var encendida = AnfitrionDeIa.Construir(db, c => c["Mensajeria:CicloHabilitado"] = "true");
        await encendida.IniciarAsync();
        var limite = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < limite && (await Foto(encendida)).Split('|').Count(f => f.Contains("Completada")) < 2)
            await Task.Delay(500);

        Assert.Equal(2, (await Foto(encendida)).Split('|').Count(f => f.Contains("Completada")));
        Assert.Equal(1, await encendida.Resumenes(p1));
        Assert.Equal(1, await encendida.Resumenes(p2));
    }
}
