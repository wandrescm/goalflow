using Microsoft.AspNetCore.HttpOverrides;

namespace GoalFlow.Api.Hosting;

public static class ForwardedHeadersExtensions
{
    // Spec 010 R-23 and C-19: only X-Forwarded-Proto is processed, so Request.Scheme stays https behind
    // Render. The client address is not derived here: the partition function of the rate limiter reads
    // its own header (R-07, R-22). Render's proxy addresses are not fixed, so in Production the trusted
    // lists are cleared; with them cleared the scheme header is accepted from any source that reaches
    // Kestrel, which is safe only because the container is reachable solely through that proxy
    // (ADR-05 2.4, C-19). ForwardLimit 1 keeps a single value.
    public static IServiceCollection AddGoalFlowForwardedHeaders(this IServiceCollection services)
    {
        services.AddOptions<ForwardedHeadersOptions>()
            .Configure<IHostEnvironment>((options, environment) =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedProto;
                options.ForwardLimit = 1;

                if (environment.IsProduction())
                {
                    options.KnownIPNetworks.Clear();
                    options.KnownProxies.Clear();
                }
            });

        return services;
    }

    public static IApplicationBuilder UseGoalFlowForwardedHeaders(this IApplicationBuilder app) =>
        app.UseForwardedHeaders();
}
