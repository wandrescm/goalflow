using GoalFlow.Application.IA;
using GoalFlow.Application.Persistencia;
using GoalFlow.Application.Torneos.Contracts;
using GoalFlow.Domain.IA;
using GoalFlow.Infrastructure.IA;
using GoalFlow.Infrastructure.Persistencia;
using GoalFlow.Infrastructure.Torneos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoalFlow.Tests.Isolation;

/// <summary>
/// Spec 011 T-06 (c), con compuertas (Spec 006 C-24): la petición marcada con el encabezado de prueba se retiene <b>inmediatamente
/// después de que <c>LeerPorClaveAsync</c> devuelve los hechos</b>, antes de leer los resúmenes. Con una sola transacción, ese punto
/// ya tiene la foto tomada; con dos, está en la primera y la segunda tomaría una foto nueva. Solo existe en el anfitrión de pruebas.
/// </summary>
public sealed class ControlDeHechosDelDetalle
{
    public const string Encabezado = "X-Prueba-Compuerta-Hechos";

    private static readonly AsyncLocal<bool> Marcada = new();
    private readonly List<string> _eventos = [];

    public TaskCompletionSource Retenida { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource Compuerta { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

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

    public void Liberar()
    {
        Anotar("liberada");
        Compuerta.TrySetResult();
    }

    /// <summary>Registra los decoradores; con <paramref name="lecturaConfirmada"/>, la lectura consistente abre <c>READ COMMITTED</c> (control negativo).</summary>
    public Action<IWebHostBuilder> Registrar(bool lecturaConfirmada = false) => b => b.ConfigureTestServices(servicios =>
    {
        servicios.AddSingleton(this);
        servicios.AddSingleton<IStartupFilter>(new MarcaDeLaPeticion());
        servicios.RemoveAll<ILectorDePartidoParaResumen>();
        servicios.AddScoped<ILectorDePartidoParaResumen>(sp =>
            new LectorConCompuerta(ActivatorUtilities.CreateInstance<LectorDePartidoParaResumen>(sp), this));
        servicios.RemoveAll<IAlmacenDeResumenes>();
        servicios.AddScoped<IAlmacenDeResumenes>(sp =>
            new AlmacenConEventos(ActivatorUtilities.CreateInstance<AlmacenDeResumenes>(sp), this));
        if (!lecturaConfirmada) return;

        servicios.RemoveAll<IUnidadDeTrabajo>();
        servicios.AddScoped<IUnidadDeTrabajo>(sp => new UnidadDeTrabajoReadCommitted(ActivatorUtilities.CreateInstance<UnidadDeTrabajo>(sp)));
    });

    private sealed class MarcaDeLaPeticion : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> siguiente) => app =>
        {
            app.Use(async (contexto, next) =>
            {
                Marcada.Value = contexto.Request.Headers.ContainsKey(Encabezado);
                await next();
            });
            siguiente(app);
        };
    }

    private sealed class LectorConCompuerta(ILectorDePartidoParaResumen interno, ControlDeHechosDelDetalle control) : ILectorDePartidoParaResumen
    {
        public Task<DatosDePartidoParaResumen?> LeerAsync(Guid partidoId, CancellationToken cancellationToken) =>
            interno.LeerAsync(partidoId, cancellationToken);

        public async Task<PartidoPorClave?> LeerPorClaveAsync(ClaveNaturalDePartido clave, CancellationToken cancellationToken)
        {
            var resultado = await interno.LeerPorClaveAsync(clave, cancellationToken);
            if (Marcada.Value)
            {
                control.Anotar("lectura-de-hechos");
                control.Anotar("retenida");
                control.Retenida.TrySetResult();
                await control.Compuerta.Task;
            }

            return resultado;
        }
    }

    private sealed class AlmacenConEventos(IAlmacenDeResumenes interno, ControlDeHechosDelDetalle control) : IAlmacenDeResumenes
    {
        public Task<bool> ExisteAsync(Guid partidoId, string huella, CancellationToken cancellationToken) =>
            interno.ExisteAsync(partidoId, huella, cancellationToken);

        public Task<bool> InsertarAsync(ResumenPartido resumen, CancellationToken cancellationToken) =>
            interno.InsertarAsync(resumen, cancellationToken);

        public Task<IReadOnlyList<ResumenPartido>> ListarDelPartidoAsync(Guid partidoId, CancellationToken cancellationToken)
        {
            if (Marcada.Value) control.Anotar("lectura-de-resumenes"); // sin compuerta
            return interno.ListarDelPartidoAsync(partidoId, cancellationToken);
        }
    }

    private sealed class UnidadDeTrabajoReadCommitted(IUnidadDeTrabajo interno) : IUnidadDeTrabajo
    {
        public Task<ITransaccion> IniciarAsync(CancellationToken cancellationToken) => interno.IniciarAsync(cancellationToken);

        // Control negativo: la «lectura consistente» abre la transacción por defecto (READ COMMITTED).
        public Task<ITransaccion> IniciarLecturaConsistenteAsync(CancellationToken cancellationToken) => interno.IniciarAsync(cancellationToken);
    }
}
