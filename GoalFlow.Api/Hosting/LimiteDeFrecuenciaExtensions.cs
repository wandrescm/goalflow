using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace GoalFlow.Api.Hosting;

/// <summary>
/// Limitador de frecuencia (Spec 010 R-10 a R-18). Un único <c>GlobalLimiter</c> encadenado, en este orden: (1) por cliente,
/// (2) global por clase (C-07). Ninguna política por endpoint: correría después del limitador global y cambiaría la contabilidad.
/// </summary>
public static class LimiteDeFrecuenciaExtensions
{
    private static readonly TimeSpan Ventana = TimeSpan.FromSeconds(60);

    private const string CuerpoDelRechazo = """{"error":"limite_de_frecuencia"}""";

    public static IServiceCollection AddGoalFlowLimiteDeFrecuencia(this IServiceCollection services)
    {
        services.AddOptions<OpcionesDeLimiteDeFrecuencia>()
            .BindConfiguration(OpcionesDeLimiteDeFrecuencia.Seccion)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<OpcionesDeLimiteDeFrecuencia>, ValidadorDeLimiteDeFrecuencia>();

        services.AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>()
            .Configure<IOptions<OpcionesDeLimiteDeFrecuencia>>((limitador, opciones) =>
            {
                var limites = opciones.Value;
                limitador.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                limitador.GlobalLimiter = PartitionedRateLimiter.CreateChained(PorCliente(limites), GlobalPorClase(limites));
                limitador.OnRejected = AlRechazar;
            });

        return services;
    }

    private static LimitesDeClase LimitesDe(OpcionesDeLimiteDeFrecuencia limites, ClaseDeRuta clase) =>
        clase == ClaseDeRuta.Admin ? limites.Admin : limites.Publica;

    private static FixedWindowRateLimiterOptions Ventanas(int permisos) => new()
    {
        PermitLimit = permisos,
        Window = Ventana,
        QueueLimit = 0,
        AutoReplenishment = true,
    };

    /// <summary>(1) Por cliente: ventana fija por (clase, IP normalizada). <c>salud</c> no se limita ni consume permisos (R-12).</summary>
    private static PartitionedRateLimiter<HttpContext> PorCliente(OpcionesDeLimiteDeFrecuencia limites) =>
        PartitionedRateLimiter.Create<HttpContext, string>(contexto =>
        {
            var clase = ParticionDeFrecuencia.ClaseDe(contexto.Request.Path);
            if (clase == ClaseDeRuta.Salud) return RateLimitPartition.GetNoLimiter("salud");

            var permisos = LimitesDe(limites, clase).PorCliente;
            return RateLimitPartition.GetFixedWindowLimiter(ParticionDeFrecuencia.ClaveDeParticion(contexto), _ => Ventanas(permisos));
        });

    /// <summary>(2) Global por clase: una partición de ventana fija por clase.</summary>
    private static PartitionedRateLimiter<HttpContext> GlobalPorClase(OpcionesDeLimiteDeFrecuencia limites) =>
        PartitionedRateLimiter.Create<HttpContext, ClaseDeRuta>(contexto =>
        {
            var clase = ParticionDeFrecuencia.ClaseDe(contexto.Request.Path);
            if (clase == ClaseDeRuta.Salud) return RateLimitPartition.GetNoLimiter(clase);

            var permisos = LimitesDe(limites, clase).Global;
            return RateLimitPartition.GetFixedWindowLimiter(clase, _ => Ventanas(permisos));
        });

    /// <summary>
    /// 429 con <c>{"error":"limite_de_frecuencia"}</c> y, si el lease que rechazó lo da, <c>Retry-After</c> en segundos enteros hacia
    /// arriba (R-13). No revela clase, partición, IP, límites ni conteos, y no escribe logs (R-16).
    /// </summary>
    private static async ValueTask AlRechazar(OnRejectedContext contexto, CancellationToken cancellationToken)
    {
        var respuesta = contexto.HttpContext.Response;
        respuesta.StatusCode = StatusCodes.Status429TooManyRequests;

        if (contexto.Lease.TryGetMetadata(MetadataName.RetryAfter, out var espera))
            respuesta.Headers.RetryAfter = Math.Ceiling(espera.TotalSeconds).ToString("0", CultureInfo.InvariantCulture);

        respuesta.ContentType = "application/json";
        await respuesta.WriteAsync(CuerpoDelRechazo, cancellationToken);
    }
}
