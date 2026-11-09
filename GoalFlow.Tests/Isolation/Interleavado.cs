using GoalFlow.Application.Torneos;
using GoalFlow.Infrastructure.Torneos;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoalFlow.Tests.Isolation;

/// <summary>
/// Control de un entrelazado de concurrencia (Spec 006 §4, C-24). Los decoradores de los dos puertos de almacén
/// (la planilla y la importación) anotan aquí lo que observan, en orden, y esperan en una compuerta después de que la
/// lectura devuelve. Una prueba prueba el entrelazado que dice con el orden observado, nunca con una barrera de peticiones.
/// Solo existe en el anfitrión de pruebas: ningún tipo de este archivo se registra en producción.
/// </summary>
public sealed class Interleavado
{
    private readonly List<string> _eventos = [];

    public Interleavado()
    {
        Hoja = new PuertoDeInterleavado("hoja", this);
        Importacion = new PuertoDeInterleavado("importacion", this);
    }

    public PuertoDeInterleavado Hoja { get; }

    public PuertoDeInterleavado Importacion { get; }

    /// <summary>Lo observado hasta ahora, en orden.</summary>
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
        Assert.True(indice >= 0, $"No se observó el evento '{evento}'. Observados: {string.Join(", ", Eventos)}");
        return indice;
    }

    /// <summary>Registra los decoradores de ambos puertos en el anfitrión de pruebas, delegando en los adaptadores reales.</summary>
    public Action<IWebHostBuilder> Registrar() => builder => builder.ConfigureTestServices(servicios =>
    {
        servicios.RemoveAll<IAlmacenDePlanilla>();
        servicios.AddScoped<IAlmacenDePlanilla>(sp =>
            new AlmacenDePlanillaConCompuerta(ActivatorUtilities.CreateInstance<AlmacenDePlanilla>(sp), Hoja));
        servicios.RemoveAll<IAlmacenDeImportacion>();
        servicios.AddScoped<IAlmacenDeImportacion>(sp =>
            new AlmacenDeImportacionConCompuerta(ActivatorUtilities.CreateInstance<AlmacenDeImportacion>(sp), Importacion));
    });

    public static TaskCompletionSource Compuerta(bool abierta)
    {
        var compuerta = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (abierta) compuerta.SetResult();
        return compuerta;
    }
}

/// <summary>Las compuertas y los contadores de un puerto. Por omisión ambas compuertas están abiertas.</summary>
public sealed class PuertoDeInterleavado(string nombre, Interleavado control)
{
    private int _lecturas;

    /// <summary>Compuerta después de que la lectura devuelve: se cierra reemplazándola por <c>Compuerta(false)</c>.</summary>
    public TaskCompletionSource TrasLeer { get; set; } = Interleavado.Compuerta(abierta: true);

    /// <summary>Compuerta antes de empezar a leer, solo para la lectura número <see cref="RetenerLaLecturaNumero"/>.</summary>
    public TaskCompletionSource AntesDeLeer { get; set; } = Interleavado.Compuerta(abierta: true);

    /// <summary>0 = ninguna. El número cuenta las lecturas del puerto desde que se creó el anfitrión.</summary>
    public int RetenerLaLecturaNumero { get; set; }

    public string Nombre => nombre;

    public int SiguienteLectura() => Interlocked.Increment(ref _lecturas);

    public void Anotar(string evento) => control.Anotar($"{evento}-{nombre}");

    public string Etiqueta(string evento, int numero) => $"{evento}-{nombre}-{numero}";

    public void AnotarEtiqueta(string evento, int numero) => control.Anotar(Etiqueta(evento, numero));
}

public sealed class AlmacenDePlanillaConCompuerta(IAlmacenDePlanilla interno, PuertoDeInterleavado puerto) : IAlmacenDePlanilla
{
    private int _numero;

    public async Task<PartidoCargado?> CargarAsync(ClaveDePartido clave, CancellationToken cancellationToken)
    {
        _numero = puerto.SiguienteLectura();
        if (puerto.RetenerLaLecturaNumero == _numero) await puerto.AntesDeLeer.Task;

        var cargado = await interno.CargarAsync(clave, cancellationToken);
        puerto.AnotarEtiqueta("lectura", _numero);
        await puerto.TrasLeer.Task;
        return cargado;
    }

    public void AgregarVersion(Domain.Torneos.Planilla planilla, IReadOnlyList<Domain.Torneos.Gol> goles, IReadOnlyList<Domain.Torneos.Tarjeta> tarjetas) =>
        interno.AgregarVersion(planilla, goles, tarjetas);

    public Task GuardarAsync(CancellationToken cancellationToken)
    {
        puerto.AnotarEtiqueta("escritura", _numero);
        return interno.GuardarAsync(cancellationToken);
    }
}

public sealed class AlmacenDeImportacionConCompuerta(IAlmacenDeImportacion interno, PuertoDeInterleavado puerto) : IAlmacenDeImportacion
{
    private int _numero;

    public async Task<EstadoGuardado> CargarAsync(
        string codigoDeTorneo, IReadOnlyCollection<string> codigosDeEquipo, IReadOnlyCollection<string> codigosDeJugador,
        CancellationToken cancellationToken)
    {
        _numero = puerto.SiguienteLectura();
        if (puerto.RetenerLaLecturaNumero == _numero) await puerto.AntesDeLeer.Task;

        var estado = await interno.CargarAsync(codigoDeTorneo, codigosDeEquipo, codigosDeJugador, cancellationToken);
        puerto.AnotarEtiqueta("lectura", _numero);
        await puerto.TrasLeer.Task;
        return estado;
    }

    public void Agregar<T>(T entidad) where T : class => interno.Agregar(entidad);

    public Task GuardarAsync(CancellationToken cancellationToken)
    {
        puerto.AnotarEtiqueta("escritura", _numero);
        return interno.GuardarAsync(cancellationToken);
    }
}
