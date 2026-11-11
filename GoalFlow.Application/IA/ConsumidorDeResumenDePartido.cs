using System.Globalization;
using GoalFlow.Application.Mensajeria;
using GoalFlow.Application.Persistencia;
using GoalFlow.Application.Torneos.Contracts;
using GoalFlow.Domain.IA;
using GoalFlow.Domain.Tenancy;
using Microsoft.Extensions.Logging;

namespace GoalFlow.Application.IA;

/// <summary>
/// A-01: el consumidor de <see cref="PartidoGuardado"/> que guarda un resumen por (partido, huella de la entrada), sea una
/// plantilla determinista o un texto del modelo, con a lo sumo tres llamadas pagadas por huella (Spec 007 R-13 a R-26).
/// Cada lectura y escritura corre en una transacción corta de la unidad de trabajo, y ninguna transacción está abierta mientras se
/// llama al proveedor (R-20). Se entrega al menos una vez: cada paso tolera la reentrega.
/// Solo registra identificadores, códigos y cifras; nunca la entrada, nombres, texto generado ni mensajes de excepción (R-27).
/// </summary>
public sealed class ConsumidorDeResumenDePartido(
    IUnidadDeTrabajo unidad,
    ILectorDePartidoParaResumen lector,
    IAlmacenDeResumenes resumenes,
    IAlmacenDePresupuesto presupuesto,
    IAlmacenDeLlamadas llamadas,
    IGeneradorDeTexto generador,
    IFuenteDeTenants fuenteDeTenants,
    OpcionesDeIA opciones,
    TimeProvider reloj,
    ILogger<ConsumidorDeResumenDePartido> logger) : IConsumidorDeEvento<PartidoGuardado>
{
    public const string NombreDelConsumidor = "ia.resumen-partido";

    public static string Nombre => NombreDelConsumidor;

    /// <summary>Lo que se sabe de la entrega una vez leída la planilla vigente.</summary>
    private sealed record Caso(
        TenantId Tenant, Guid PartidoId, Guid TorneoId, int Version, string Huella, EntradaCanonica Entrada, int Intento)
    {
        public string Prefijo => Huella[..12];
    }

    public async Task ProcesarAsync(EntregaDeEvento<PartidoGuardado> entrega, CancellationToken cancellationToken)
    {
        var evento = entrega.Evento;

        // R-13: primero se lee la planilla vigente, en su propia transacción.
        DatosDePartidoParaResumen? datos;
        await using (var lectura = await unidad.IniciarAsync(cancellationToken))
        {
            datos = await lector.LeerAsync(evento.AgregadoId, cancellationToken);
            await lectura.ConfirmarAsync(cancellationToken);
        }

        if (datos is null)
        {
            logger.LogInformation(
                "A-01 sin partido: tenant {Tenant}, partido {Partido}, versión {Version}, resultado {Resultado}.",
                evento.TenantId.Value, evento.AgregadoId, evento.Version, "partido_ausente");
            return;
        }

        if (datos.Version > evento.Version)
        {
            logger.LogInformation(
                "A-01 entrega obsoleta: tenant {Tenant}, partido {Partido}, versión {Version}, vigente {Vigente}, resultado {Resultado}.",
                evento.TenantId.Value, evento.AgregadoId, evento.Version, datos.Version, "obsoleta");
            return;
        }

        var clasificacion = ClasificadorDeResumen.Clasificar(datos);
        if (clasificacion == ClasificacionDeResumen.Ninguno)
        {
            logger.LogInformation(
                "A-01 sin resumen: tenant {Tenant}, partido {Partido}, versión {Version}, clasificación {Clasificacion}, resultado {Resultado}.",
                evento.TenantId.Value, evento.AgregadoId, datos.Version, clasificacion, "ninguno");
            return;
        }

        var entrada = EntradaCanonica.Construir(datos);
        var caso = new Caso(evento.TenantId, evento.AgregadoId, datos.TorneoId, datos.Version, entrada.Huella(), entrada, entrega.Intento);

        if (clasificacion == ClasificacionDeResumen.Plantilla)
            await GuardarPlantillaAsync(caso, cancellationToken);
        else
            await ProcesarNarrativaAsync(caso, cancellationToken);
    }

    // ---- R-15: las plantillas no tocan el presupuesto ni crean llamadas ----

    private async Task GuardarPlantillaAsync(Caso caso, CancellationToken cancellationToken)
    {
        var texto = TextoOFalloPermanente(() => PlantillasDeResumen.Plantilla(caso.Entrada));

        await using var transaccion = await unidad.IniciarAsync(cancellationToken);
        var nuevo = await resumenes.InsertarAsync(
            NuevoResumen(caso, OrigenDeResumen.Plantilla, EstadoDeResumen.PorVerificar, null, texto, null, null), cancellationToken);
        await transaccion.ConfirmarAsync(cancellationToken);

        Registrar(caso, ClasificacionDeResumen.Plantilla, nuevo ? "plantilla" : "plantilla_ya_guardada");
    }

    // ---- R-16 a R-25 ----

    private async Task ProcesarNarrativaAsync(Caso caso, CancellationToken cancellationToken)
    {
        // R-16 y R-17: una sola lectura corta.
        bool existe;
        var previas = 0;
        await using (var lectura = await unidad.IniciarAsync(cancellationToken))
        {
            existe = await resumenes.ExisteAsync(caso.PartidoId, caso.Huella, cancellationToken);
            if (!existe) previas = await llamadas.ContarAsync(caso.PartidoId, caso.Huella, cancellationToken);
            await lectura.ConfirmarAsync(cancellationToken);
        }

        if (existe)
        {
            Registrar(caso, ClasificacionDeResumen.Narrativa, "resumen_ya_guardado");
            return;
        }

        if (previas >= opciones.MaximoDeLlamadasPorHuella)
        {
            await GuardarBorradorAsync(caso, MotivoDeBorrador.LimiteDeLlamadas, null, cancellationToken);
            return;
        }

        // R-18: la cota global se cumple por construcción; si no, no se reserva (falla cerrado).
        var ligas = (await fuenteDeTenants.ListarAsync(cancellationToken)).Count;
        if (ligas * opciones.TopeMensualPorLigaMicroUsd > opciones.TopeMensualGlobalMicroUsd)
        {
            logger.LogWarning(
                "A-01 sin reservar: {Ligas} ligas por el tope por liga {TopePorLiga} exceden el tope global {TopeGlobal}. Tenant {Tenant}, partido {Partido}, versión {Version}.",
                ligas, opciones.TopeMensualPorLigaMicroUsd, opciones.TopeMensualGlobalMicroUsd, caso.Tenant.Value, caso.PartidoId, caso.Version);
            await GuardarBorradorAsync(caso, MotivoDeBorrador.Presupuesto, null, cancellationToken);
            return;
        }

        // R-19 (0): el tamaño y el conteo, sin reservar nada.
        if (caso.Entrada.Bytes().Length > opciones.MaximoDeBytesDeEntrada)
        {
            await GuardarBorradorAsync(caso, MotivoDeBorrador.EntradaExcedida, null, cancellationToken);
            return;
        }

        var solicitud = new SolicitudDeGeneracion(
            InstruccionesDeResumen.TextoDeSistema, caso.Entrada.Json(), opciones.MaxTokensDeSalida, opciones.Razonamiento);

        int contados;
        try
        {
            contados = await ConDeadlineAsync(token => generador.ContarAsync(solicitud, token), cancellationToken);
        }
        catch (Exception ex) when (EsDeApagado(ex, cancellationToken))
        {
            throw;
        }
        catch (RespuestaConErrorException ex)
        {
            // Contar es gratis: no hay nada que conciliar ni reservado (R-21, última frase). Se clasifica como al generar, por estado y tipo.
            switch (ClasificadorDeErrorDelProveedor.Clasificar(ex))
            {
                case DesenlaceDeError.BorradorPorSolicitudInvalida:
                    await GuardarBorradorAsync(caso, MotivoDeBorrador.SolicitudInvalida, null, cancellationToken);
                    return;
                case DesenlaceDeError.CredencialInvalida:
                    throw Permanente(caso, ClasificadorDeErrorDelProveedor.CodigoDeCredencialInvalida, ex);
                case DesenlaceDeError.FacturacionDelProveedor:
                    throw Permanente(caso, ClasificadorDeErrorDelProveedor.CodigoDeFacturacionDelProveedor, ex);
                default: // (c) y (f): reintentable, sin nada que escribir
                    throw ReintentableConRegistro(caso, $"conteo_{ClasificadorDeErrorDelProveedor.CodigoDeResultado(ex)}", ex);
            }
        }
        catch (Exception ex)
        {
            throw ReintentableConRegistro(caso, "conteo_incierto", ex);
        }

        if (contados > opciones.MaximoDeTokensDeEntrada)
        {
            await GuardarBorradorAsync(caso, MotivoDeBorrador.EntradaExcedida, null, cancellationToken);
            return;
        }

        // R-19: la reserva, en una transacción corta y sin ninguna llamada externa dentro.
        var monto = CalculoDeCosto.Reserva(contados, opciones.Precios);
        var mes = Mes(reloj.GetUtcNow());
        var llamada = await ReservarAsync(caso, mes, monto, cancellationToken);
        if (llamada.Motivo is { } motivo)
        {
            await GuardarBorradorAsync(caso, motivo, null, cancellationToken);
            return;
        }

        var (llamadaId, numero) = (llamada.Id, llamada.Numero);

        // R-20: la llamada, con la transacción ya confirmada y cerrada.
        RespuestaGenerada respuesta;
        try
        {
            respuesta = await ConDeadlineAsync(token => generador.GenerarAsync(solicitud, token), cancellationToken);
        }
        catch (Exception ex) when (EsDeApagado(ex, cancellationToken))
        {
            throw; // ADR-02 §2.5: nada se escribe; la llamada sigue Reservada porque pudo cobrarse.
        }
        catch (RespuestaConErrorException ex)
        {
            await ResolverErrorDeRespuestaAsync(caso, llamadaId, numero, ex, cancellationToken);
            return;
        }
        catch (SalidaInvalidaException ex)
        {
            await ConciliarAsync(caso, llamadaId, numero, ex.Uso, "salida_invalida", cancellationToken);
            throw ReintentableConRegistro(caso, "salida_invalida", ex);
        }
        catch (Exception ex)
        {
            // (f) Sin respuesta: no se escribe nada en la llamada ni en el contador; la reserva se queda contada.
            throw ReintentableConRegistro(caso, "resultado_incierto", ex, numero);
        }

        await ResolverRespuestaAsync(caso, llamadaId, numero, respuesta, cancellationToken);
    }

    private sealed record Reserva(Guid Id, int Numero, MotivoDeBorrador? Motivo);

    private async Task<Reserva> ReservarAsync(Caso caso, string mes, long monto, CancellationToken cancellationToken)
    {
        await using var transaccion = await unidad.IniciarAsync(cancellationToken);

        // (a) el contador del mes, con el tope actual por liga.
        await presupuesto.AsegurarContadorAsync(mes, opciones.TopeMensualPorLigaMicroUsd, cancellationToken);

        // El número y el tope de llamadas se calculan aquí, en la transacción: otro trabajador pudo guardar una llamada
        // entre la lectura de R-17 y esta reserva (el índice único garantiza que no se repita el número).
        var numero = await llamadas.ContarAsync(caso.PartidoId, caso.Huella, cancellationToken) + 1;
        if (numero > opciones.MaximoDeLlamadasPorHuella)
            return new Reserva(Guid.Empty, numero, MotivoDeBorrador.LimiteDeLlamadas); // sin confirmar

        // (b) el compare-and-swap; sin saldo, la transacción se libera sin confirmar y el partido recibe el borrador.
        if (!await presupuesto.ReservarAsync(mes, monto, cancellationToken))
            return new Reserva(Guid.Empty, numero, MotivoDeBorrador.Presupuesto);

        // (c) la fila de la llamada.
        var id = Guid.CreateVersion7();
        try
        {
            await llamadas.InsertarReservadaAsync(
                new LlamadaIa(id, caso.Tenant, caso.TorneoId, caso.PartidoId, caso.Version, caso.Huella, numero, caso.Intento, mes, monto),
                cancellationToken);
        }
        catch (NumeroDeLlamadaDuplicadoException ex)
        {
            // La transacción se libera sin confirmar, con el cambio del contador incluido (T-12).
            throw ReintentableConRegistro(caso, "llamada_concurrente", ex, numero);
        }

        // (d)
        await transaccion.ConfirmarAsync(cancellationToken);
        return new Reserva(id, numero, null);
    }

    private async Task ResolverRespuestaAsync(
        Caso caso, Guid llamadaId, int numero, RespuestaGenerada respuesta, CancellationToken cancellationToken)
    {
        var uso = new UsoReportado(respuesta.TokensEntrada, respuesta.TokensSalida);

        // (h) Un rechazo del modelo: se concilia y se guarda el borrador, y se completa.
        if (respuesta.StopReason == "refusal")
        {
            await GuardarBorradorAsync(
                caso, MotivoDeBorrador.RechazoDelModelo,
                () => ConciliarEnTransaccionAsync(caso, llamadaId, numero, uso, "rechazo_del_modelo", cancellationToken),
                cancellationToken);
            return;
        }

        // (b) Una salida inválida, incluido el corte por max_tokens: se concilia y se reintenta.
        var validacion = respuesta.StopReason == "max_tokens"
            ? new ResultadoDeSalida(false, null)
            : ValidadorDeSalidaDelModelo.Validar(respuesta.Texto);
        if (!validacion.EsValida)
        {
            var codigo = respuesta.StopReason == "max_tokens" ? "limite_de_tokens" : "salida_invalida";
            await ConciliarAsync(caso, llamadaId, numero, uso, codigo, cancellationToken);
            throw ReintentableConRegistro(caso, codigo, null, numero);
        }

        // (a) R-22: la conciliación y el resumen, en una sola transacción.
        await using var transaccion = await unidad.IniciarAsync(cancellationToken);
        await ConciliarEnTransaccionAsync(caso, llamadaId, numero, uso, "exito", cancellationToken);
        var nuevo = await resumenes.InsertarAsync(
            NuevoResumen(
                caso, OrigenDeResumen.Modelo, EstadoDeResumen.PorVerificar, null, validacion.Texto!,
                opciones.Modelo, InstruccionesDeResumen.VersionDelPrompt, llamadaId),
            cancellationToken);
        await transaccion.ConfirmarAsync(cancellationToken);

        Registrar(caso, ClasificacionDeResumen.Narrativa, nuevo ? "resumen_del_modelo" : "resumen_ya_guardado", numero, uso);
    }

    // R-21 (ADR-12 §2.6): se clasifica por el estado HTTP y el error.type documentado, nunca por el mensaje.
    private async Task ResolverErrorDeRespuestaAsync(
        Caso caso, Guid llamadaId, int numero, RespuestaConErrorException error, CancellationToken cancellationToken)
    {
        var codigo = ClasificadorDeErrorDelProveedor.CodigoDeResultado(error);
        switch (ClasificadorDeErrorDelProveedor.Clasificar(error))
        {
            case DesenlaceDeError.BorradorPorSolicitudInvalida: // (d) nuestra solicitud está mal: se concilia y se guarda el borrador
                await GuardarBorradorAsync(
                    caso, MotivoDeBorrador.SolicitudInvalida,
                    () => ConciliarEnTransaccionAsync(caso, llamadaId, numero, error.Uso, codigo, cancellationToken),
                    cancellationToken);
                return;

            case DesenlaceDeError.CredencialInvalida: // (e) credenciales: se concilia en 0 y el fallo es permanente
                await ConciliarAsync(caso, llamadaId, numero, null, codigo, cancellationToken);
                throw Permanente(caso, ClasificadorDeErrorDelProveedor.CodigoDeCredencialInvalida, error);

            case DesenlaceDeError.FacturacionDelProveedor: // (e2) facturación: se concilia en 0 y el fallo es permanente
                await ConciliarAsync(caso, llamadaId, numero, null, codigo, cancellationToken);
                throw Permanente(caso, ClasificadorDeErrorDelProveedor.CodigoDeFacturacionDelProveedor, error);

            case DesenlaceDeError.Incierto: // (f) 504, timeout_error o un estado desconocido: NO se escribe nada, tampoco habiendo respuesta
                throw ReintentableConRegistro(caso, codigo, error, numero);

            default: // (c) 409, 429, 529, 500 (no timeout_error) y otro 5xx salvo 504: se concilia y se reintenta
                await ConciliarAsync(caso, llamadaId, numero, error.Uso, codigo, cancellationToken);
                throw ReintentableConRegistro(caso, codigo, error, numero);
        }
    }

    // ---- Conciliación ----

    private async Task ConciliarAsync(
        Caso caso, Guid llamadaId, int numero, UsoReportado? uso, string codigo, CancellationToken cancellationToken)
    {
        await using var transaccion = await unidad.IniciarAsync(cancellationToken);
        await ConciliarEnTransaccionAsync(caso, llamadaId, numero, uso, codigo, cancellationToken);
        await transaccion.ConfirmarAsync(cancellationToken);
    }

    private async Task ConciliarEnTransaccionAsync(
        Caso caso, Guid llamadaId, int numero, UsoReportado? uso, string codigo, CancellationToken cancellationToken)
    {
        var (entrada, salida) = (uso?.TokensEntrada ?? 0, uso?.TokensSalida ?? 0);
        var costo = CalculoDeCosto.CostoReal(entrada, salida, opciones.Precios);

        var concilio = await llamadas.ConciliarAsync(llamadaId, entrada, salida, costo, codigo, cancellationToken);

        logger.LogInformation(
            "A-01 llamada conciliada: tenant {Tenant}, partido {Partido}, versión {Version}, huella {Huella}, llamada {Numero}, intento {Intento}, tokens de entrada {TokensEntrada}, tokens de salida {TokensSalida}, costo {Costo}, resultado {Resultado}, conciliada {Concilio}.",
            caso.Tenant.Value, caso.PartidoId, caso.Version, caso.Prefijo, numero, caso.Intento, entrada, salida, costo, codigo, concilio);
    }

    // ---- R-25: el borrador extendido ----

    private async Task GuardarBorradorAsync(
        Caso caso, MotivoDeBorrador motivo, Func<Task>? antes, CancellationToken cancellationToken)
    {
        var texto = TextoOFalloPermanente(() => PlantillasDeResumen.BorradorExtendido(caso.Entrada, opciones.LongitudMaximaDeBorrador));

        await using var transaccion = await unidad.IniciarAsync(cancellationToken);
        if (antes is not null) await antes();
        var nuevo = await resumenes.InsertarAsync(
            NuevoResumen(caso, OrigenDeResumen.Plantilla, EstadoDeResumen.Borrador, motivo, texto, null, null), cancellationToken);
        await transaccion.ConfirmarAsync(cancellationToken);

        Registrar(caso, ClasificacionDeResumen.Narrativa, nuevo ? $"borrador_{motivo.Codigo()}" : "borrador_ya_guardado");
    }

    // ---- Ayudas ----

    private static ResumenPartido NuevoResumen(
        Caso caso, OrigenDeResumen origen, EstadoDeResumen estado, MotivoDeBorrador? motivo, string texto,
        string? modelo, string? versionPrompt, Guid? llamadaId = null) =>
        new(Guid.CreateVersion7(), caso.Tenant, caso.TorneoId, caso.PartidoId, caso.Version, caso.Huella,
            origen, estado, motivo, texto, modelo, versionPrompt, llamadaId);

    private static string TextoOFalloPermanente(Func<string> construir)
    {
        try
        {
            return construir();
        }
        catch (PlanillaIncoherenteException ex)
        {
            throw new FalloPermanenteException(PlanillaIncoherenteException.CodigoDeRazon, ex);
        }
    }

    private static string Mes(DateTimeOffset instante) => instante.UtcDateTime.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    /// <summary>
    /// Ejecuta la llamada con el límite de <c>TimeoutDeLlamada</c>. Si vence ese límite (y no el del consumidor ni el apagado) es un
    /// resultado incierto, igual que una conexión perdida.
    /// </summary>
    private async Task<T> ConDeadlineAsync<T>(Func<CancellationToken, Task<T>> llamar, CancellationToken cancellationToken)
    {
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limite.CancelAfter(opciones.TimeoutDeLlamada);
        try
        {
            return await llamar(limite.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && limite.IsCancellationRequested)
        {
            throw new ResultadoInciertoException("Se agotó el tiempo de la llamada.");
        }
    }

    private static bool EsDeApagado(Exception ex, CancellationToken cancellationToken) =>
        ex is OperationCanceledException && cancellationToken.IsCancellationRequested;

    // Un fallo permanente de Mensajería con un código de razón estable. El registro lleva el código, nunca el mensaje del proveedor (R-27).
    private FalloPermanenteException Permanente(Caso caso, string codigoDeRazon, Exception interna)
    {
        logger.LogError(
            "A-01 fallo permanente: tenant {Tenant}, partido {Partido}, versión {Version}, resultado {Resultado}.",
            caso.Tenant.Value, caso.PartidoId, caso.Version, codigoDeRazon);
        return new FalloPermanenteException(codigoDeRazon, interna);
    }

    private FalloReintentableException ReintentableConRegistro(Caso caso, string codigo, Exception? interna, int? numero = null)
    {
        logger.LogWarning(
            "A-01 fallo reintentable: tenant {Tenant}, partido {Partido}, versión {Version}, huella {Huella}, llamada {Numero}, intento {Intento}, resultado {Resultado}, tipo {TipoDeExcepcion}.",
            caso.Tenant.Value, caso.PartidoId, caso.Version, caso.Prefijo, numero, caso.Intento, codigo, interna?.GetType().Name);
        return new FalloReintentableException(codigo, interna);
    }

    private void Registrar(Caso caso, ClasificacionDeResumen clasificacion, string resultado, int? numero = null, UsoReportado? uso = null) =>
        logger.LogInformation(
            "A-01 completada: tenant {Tenant}, partido {Partido}, versión {Version}, huella {Huella}, clasificación {Clasificacion}, llamada {Numero}, intento {Intento}, tokens de entrada {TokensEntrada}, tokens de salida {TokensSalida}, resultado {Resultado}.",
            caso.Tenant.Value, caso.PartidoId, caso.Version, caso.Prefijo, clasificacion, numero, caso.Intento,
            uso?.TokensEntrada, uso?.TokensSalida, resultado);
}
