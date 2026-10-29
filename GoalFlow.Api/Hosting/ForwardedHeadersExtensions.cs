using Microsoft.AspNetCore.HttpOverrides;

namespace GoalFlow.Api.Hosting;

public static class ForwardedHeadersExtensions
{
    // Render's proxy addresses are not fixed, so in Production the trusted
    // lists are cleared. Safe only because the container is reachable solely
    // through that proxy (ADR-05 2.4). ForwardLimit 1 keeps only the entry the
    // proxy appended, so a client-injected X-Forwarded-For value is ignored.
    public static IServiceCollection AddGoalFlowForwardedHeaders(this IServiceCollection services)
    {
        services.AddOptions<ForwardedHeadersOptions>()
            .Configure<IHostEnvironment>((options, environment) =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
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
