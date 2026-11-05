using System.Reflection;
using GoalFlow.Application.Mensajeria;
using GoalFlow.Domain.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace GoalFlow.Infrastructure.Mensajeria;

/// <summary>
/// Un consumidor registrado en este host. El ciclo solo reclama entregas de estos nombres (Spec 004 R-23, R-45).
/// </summary>
public sealed record RegistroDeConsumidor(
    string NombreDelEvento,
    string Nombre,
    Func<IServiceProvider, EntregaEvento, int, CancellationToken, Task> Procesar)
{
    public static RegistroDeConsumidor Crear<TEvento, TConsumidor>(string? nombre)
        where TEvento : IEventoDeIntegracion
        where TConsumidor : IConsumidorDeEvento<TEvento>
    {
        // El evento se reconstruye desde la fila con (TenantId, AgregadoId, Version); falla al registrar, no al entregar.
        var constructor = typeof(TEvento).GetConstructor(
                BindingFlags.Public | BindingFlags.Instance, [typeof(TenantId), typeof(Guid), typeof(int)])
            ?? throw new InvalidOperationException(
                $"{typeof(TEvento).Name} necesita un constructor público (TenantId, Guid, int) para reconstruirse desde la fila.");

        return new RegistroDeConsumidor(
            TEvento.NombreDelEvento,
            nombre ?? TConsumidor.Nombre,
            (proveedor, fila, intento, cancellationToken) =>
            {
                var evento = (TEvento)constructor.Invoke([fila.TenantId, fila.AgregadoId, fila.VersionDelAgregado]);
                return proveedor.GetRequiredService<TConsumidor>()
                    .ProcesarAsync(new EntregaDeEvento<TEvento>(evento, intento), cancellationToken);
            });
    }
}
