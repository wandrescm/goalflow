using Microsoft.Extensions.Options;

namespace GoalFlow.Infrastructure.Mensajeria;

/// <summary>Sección <c>Mensajeria</c>. Valores iniciales del ADR-02 §2.8: son configuración, no garantías de latencia.</summary>
public class OpcionesDeMensajeria
{
    public const string Seccion = "Mensajeria";

    public bool CicloHabilitado { get; set; } = true;

    public int CapacidadDelCanal { get; set; } = 64;

    public TimeSpan PeriodoDelTemporizador { get; set; } = TimeSpan.FromSeconds(60);

    public int LotePorTenant { get; set; } = 10;

    public TimeSpan DuracionDelLease { get; set; } = TimeSpan.FromMinutes(5);

    public TimeSpan TimeoutDelConsumidor { get; set; } = TimeSpan.FromMinutes(2);

    public int MaximoDeIntentos { get; set; } = 5;

    public TimeSpan BackoffInicial { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan BackoffMaximo { get; set; } = TimeSpan.FromMinutes(30);
}

/// <summary>Spec 004 R-37 y R-38: el host no arranca con una configuración que no puede cumplirse.</summary>
public sealed class ValidadorDeOpcionesDeMensajeria(IEnumerable<RegistroDeConsumidor> consumidores)
    : IValidateOptions<OpcionesDeMensajeria>
{
    public ValidateOptionsResult Validate(string? name, OpcionesDeMensajeria o)
    {
        var errores = new List<string>();

        if (o.CapacidadDelCanal <= 0) errores.Add($"{nameof(o.CapacidadDelCanal)} debe ser positivo.");
        if (o.PeriodoDelTemporizador <= TimeSpan.Zero) errores.Add($"{nameof(o.PeriodoDelTemporizador)} debe ser positivo.");
        if (o.LotePorTenant <= 0) errores.Add($"{nameof(o.LotePorTenant)} debe ser positivo.");
        if (o.DuracionDelLease <= TimeSpan.Zero) errores.Add($"{nameof(o.DuracionDelLease)} debe ser positivo.");
        if (o.TimeoutDelConsumidor <= TimeSpan.Zero) errores.Add($"{nameof(o.TimeoutDelConsumidor)} debe ser positivo.");
        if (o.MaximoDeIntentos <= 0) errores.Add($"{nameof(o.MaximoDeIntentos)} debe ser positivo.");
        if (o.BackoffInicial <= TimeSpan.Zero) errores.Add($"{nameof(o.BackoffInicial)} debe ser positivo.");
        if (o.BackoffMaximo <= TimeSpan.Zero) errores.Add($"{nameof(o.BackoffMaximo)} debe ser positivo.");
        if (o.TimeoutDelConsumidor >= o.DuracionDelLease)
            errores.Add($"{nameof(o.TimeoutDelConsumidor)} debe ser menor que {nameof(o.DuracionDelLease)}.");

        var repetidos = consumidores.GroupBy(c => c.Nombre).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (repetidos.Count > 0)
            errores.Add($"Nombres de consumidor repetidos: {string.Join(", ", repetidos)}.");

        return errores.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errores);
    }
}
