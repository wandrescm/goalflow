using GoalFlow.Application.IA;
using GoalFlow.Domain.IA;
using GoalFlow.Infrastructure.IA;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoalFlow.Tests.IA;

/// <summary>
/// Control de un entrelazado de concurrencia de IA (Spec 007 §4, patrón de la Spec 006 C-24). Los decoradores de los puertos del
/// presupuesto, las llamadas y los resúmenes anotan aquí lo que observan, en orden, y esperan en compuertas que la prueba abre.
/// Solo existen en el anfitrión de pruebas: ningún tipo de este archivo se registra en producción.
/// </summary>
public sealed class ControlDeIa
{
    private readonly List<string> _eventos = [];
    private int _asegurar;
    private int _llegadasAReservar;
    private int _insertarLlamada;
    private int _insertarResumenDelModelo;

    /// <summary>Cuántos trabajadores deben llegar a <c>ReservarAsync</c> antes de que cualquiera avance (0 = sin barrera).</summary>
    public int LlegadasEsperadasParaReservar { get; set; }

    /// <summary>Compuerta que retiene al primer trabajador justo después de que <c>AsegurarContadorAsync</c> terminó, con su transacción abierta.</summary>
    public TaskCompletionSource? TrasAsegurarElPrimero { get; set; }

    /// <summary>Compuerta que retiene al primer trabajador después de insertar su fila de llamada, sin confirmar.</summary>
    public TaskCompletionSource? TrasInsertarLaPrimeraLlamada { get; set; }

    /// <summary>Compuerta que retiene al primer trabajador después de insertar un resumen del modelo, sin confirmar.</summary>
    public TaskCompletionSource? TrasInsertarElPrimerResumenDelModelo { get; set; }

    public TaskCompletionSource BarreraDeReserva { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public IReadOnlyList<string> Eventos
    {
        get
        {
            lock (_eventos) return [.. _eventos];
        }
    }

    public void Anotar(string evento)
    {
        lock (_eventos) _eventos.Add(evento);
    }

    public int Posicion(string evento)
    {
        var indice = Eventos.ToList().IndexOf(evento);
        Assert.True(indice >= 0, $"No se observó '{evento}'. Observados: {string.Join(", ", Eventos)}");
        return indice;
    }

    public static TaskCompletionSource Compuerta() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal int NuevoAsegurar() => Interlocked.Increment(ref _asegurar);

    internal int NuevaLlegadaAReservar() => Interlocked.Increment(ref _llegadasAReservar);

    internal int NuevaLlamada() => Interlocked.Increment(ref _insertarLlamada);

    internal int NuevoResumenDelModelo() => Interlocked.Increment(ref _insertarResumenDelModelo);

    /// <summary>Registra los decoradores de los tres puertos, delegando en los adaptadores reales.</summary>
    public Action<IServiceCollection> Registrar() => servicios =>
    {
        servicios.AddSingleton(this);
        servicios.RemoveAll<IAlmacenDePresupuesto>();
        servicios.AddScoped<IAlmacenDePresupuesto>(sp =>
            new AlmacenDePresupuestoConCompuerta(ActivatorUtilities.CreateInstance<AlmacenDePresupuesto>(sp), this));
        servicios.RemoveAll<IAlmacenDeLlamadas>();
        servicios.AddScoped<IAlmacenDeLlamadas>(sp =>
            new AlmacenDeLlamadasConCompuerta(ActivatorUtilities.CreateInstance<AlmacenDeLlamadas>(sp), this));
        servicios.RemoveAll<IAlmacenDeResumenes>();
        servicios.AddScoped<IAlmacenDeResumenes>(sp =>
            new AlmacenDeResumenesConCompuerta(ActivatorUtilities.CreateInstance<AlmacenDeResumenes>(sp), this));
    };
}

public sealed class AlmacenDePresupuestoConCompuerta(IAlmacenDePresupuesto interno, ControlDeIa control) : IAlmacenDePresupuesto
{
    public async Task AsegurarContadorAsync(string mes, long topePorLiga, CancellationToken cancellationToken)
    {
        var n = control.NuevoAsegurar();
        control.Anotar($"asegurar-inicio-{n}");
        await interno.AsegurarContadorAsync(mes, topePorLiga, cancellationToken);
        control.Anotar($"asegurar-fin-{n}");
        if (n == 1 && control.TrasAsegurarElPrimero is { } compuerta) await compuerta.Task;
    }

    public async Task<bool> ReservarAsync(string mes, long monto, CancellationToken cancellationToken)
    {
        // Barrera: ningún trabajador reserva hasta que todos los esperados llegaron (después de leer el contador).
        control.Anotar("reservar-llego");
        if (control.LlegadasEsperadasParaReservar > 0
            && control.NuevaLlegadaAReservar() == control.LlegadasEsperadasParaReservar)
            control.BarreraDeReserva.SetResult();
        if (control.LlegadasEsperadasParaReservar > 0) await control.BarreraDeReserva.Task;

        control.Anotar("reservar-inicio");
        var reservo = await interno.ReservarAsync(mes, monto, cancellationToken);
        control.Anotar(reservo ? "reservar-fin-exito" : "reservar-fin-sin-saldo");
        return reservo;
    }
}

public sealed class AlmacenDeLlamadasConCompuerta(IAlmacenDeLlamadas interno, ControlDeIa control) : IAlmacenDeLlamadas
{
    public Task<int> ContarAsync(Guid partidoId, string huella, CancellationToken cancellationToken) =>
        interno.ContarAsync(partidoId, huella, cancellationToken);

    public async Task InsertarReservadaAsync(LlamadaIa llamada, CancellationToken cancellationToken)
    {
        var n = control.NuevaLlamada();
        await interno.InsertarReservadaAsync(llamada, cancellationToken);
        control.Anotar($"llamada-insertada-{n}-numero-{llamada.Numero}");
        if (n == 1 && control.TrasInsertarLaPrimeraLlamada is { } compuerta) await compuerta.Task;
    }

    public Task<bool> ConciliarAsync(
        Guid llamadaId, int tokensEntrada, int tokensSalida, long costo, string codigoDeResultado, CancellationToken cancellationToken) =>
        interno.ConciliarAsync(llamadaId, tokensEntrada, tokensSalida, costo, codigoDeResultado, cancellationToken);
}

public sealed class AlmacenDeResumenesConCompuerta(IAlmacenDeResumenes interno, ControlDeIa control) : IAlmacenDeResumenes
{
    public Task<bool> ExisteAsync(Guid partidoId, string huella, CancellationToken cancellationToken) =>
        interno.ExisteAsync(partidoId, huella, cancellationToken);

    public async Task<bool> InsertarAsync(ResumenPartido resumen, CancellationToken cancellationToken)
    {
        var insertado = await interno.InsertarAsync(resumen, cancellationToken);
        if (resumen.Origen != OrigenDeResumen.Modelo) return insertado;

        var n = control.NuevoResumenDelModelo();
        control.Anotar($"resumen-del-modelo-{n}-{(insertado ? "insertado" : "duplicado")}");
        if (n == 1 && control.TrasInsertarElPrimerResumenDelModelo is { } compuerta) await compuerta.Task;
        return insertado;
    }
}
