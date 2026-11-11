using GoalFlow.Application.IA;
using GoalFlow.Application.Persistencia;
using GoalFlow.Application.Tenancy;
using GoalFlow.Domain.IA;
using GoalFlow.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GoalFlow.Infrastructure.IA;

/// <summary>
/// Las llamadas pagadas sobre EF Core (Spec 007 R-02, R-19, R-23). La conciliación son dos <c>ExecuteUpdateAsync</c>
/// condicionados, el segundo solo si el primero afectó una fila, y exige una transacción activa (R-26).
/// </summary>
public sealed class AlmacenDeLlamadas(
    GoalFlowDbContext contexto, ITenantContext tenantContext, ILogger<AlmacenDeLlamadas> logger) : IAlmacenDeLlamadas
{
    public const string SufijoDeExceso = "_exceso";

    public async Task<int> ContarAsync(Guid partidoId, string huella, CancellationToken cancellationToken)
    {
        ExigirTransaccion();
        return await contexto.LlamadasIa.CountAsync(l => l.PartidoId == partidoId && l.Huella == huella, cancellationToken);
    }

    public async Task InsertarReservadaAsync(LlamadaIa llamada, CancellationToken cancellationToken)
    {
        ExigirTransaccion();
        contexto.LlamadasIa.Add(llamada);
        try
        {
            await contexto.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ErroresDeBase.EsUnicidadDe(ex, ConfiguracionDeIa.IndiceDeNumeroDeLlamada))
        {
            contexto.Entry(llamada).State = EntityState.Detached;
            throw new NumeroDeLlamadaDuplicadoException(ex);
        }
    }

    public async Task<bool> ConciliarAsync(
        Guid llamadaId, int tokensEntrada, int tokensSalida, long costo, string codigoDeResultado, CancellationToken cancellationToken)
    {
        ExigirTransaccion();

        var datos = await contexto.LlamadasIa.AsNoTracking()
            .Where(l => l.Id == llamadaId)
            .Select(l => new { l.Mes, l.Reserva })
            .SingleOrDefaultAsync(cancellationToken);
        if (datos is null) return false;

        // R-23: un exceso se registra completo, con el resultado marcado, y deja una advertencia.
        var exceso = costo > datos.Reserva;
        var resultado = exceso ? codigoDeResultado + SufijoDeExceso : codigoDeResultado;
        var (mes, reserva) = (datos.Mes, datos.Reserva);

        var afectadas = await contexto.LlamadasIa
            .Where(l => l.Id == llamadaId && l.Estado == EstadoDeLlamada.Reservada)
            .ExecuteUpdateAsync(s => s
                .SetProperty(l => l.Estado, EstadoDeLlamada.Conciliada)
                .SetProperty(l => l.TokensEntrada, tokensEntrada)
                .SetProperty(l => l.TokensSalida, tokensSalida)
                .SetProperty(l => l.Costo, costo)
                .SetProperty(l => l.Resultado, resultado)
                .SetProperty(l => l.ConciliadaEn, _ => DateTime.UtcNow), cancellationToken);
        if (afectadas != 1) return false;

        // El contador del mes de la llamada, sea cual sea el mes actual (R-23, T-30).
        var contador = await contexto.PresupuestosIa
            .Where(c => c.Mes == mes)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Reservado, c => c.Reservado - reserva)
                .SetProperty(c => c.Consumido, c => c.Consumido + costo), cancellationToken);
        if (contador != 1)
            throw new InvalidOperationException("La llamada reservada no tiene contador en su mes.");

        if (exceso)
            logger.LogWarning(
                "Exceso de costo en una llamada de IA: tenant {Tenant}, llamada {Llamada}, mes {Mes}, reserva {Reserva}, costo {Costo}, tokens de entrada {TokensEntrada}, tokens de salida {TokensSalida}, resultado {Resultado}.",
                tenantContext.TieneTenant ? tenantContext.Current.Value : Guid.Empty, llamadaId, mes, reserva, costo,
                tokensEntrada, tokensSalida, resultado);

        return true;
    }

    private void ExigirTransaccion()
    {
        if (contexto.Database.CurrentTransaction is null)
            throw new TransaccionRequeridaException();
    }
}
