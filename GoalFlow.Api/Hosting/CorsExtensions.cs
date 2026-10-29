namespace GoalFlow.Api.Hosting;

public static class CorsExtensions
{
    public const string AllowedOriginsKey = "Cors:AllowedOrigins";

    // An empty or missing list allows no cross-origin request at all.
    public static IServiceCollection AddGoalFlowCors(this IServiceCollection services)
    {
        services.AddCors();
        services.AddOptions<Microsoft.AspNetCore.Cors.Infrastructure.CorsOptions>()
            .Configure<IConfiguration>((options, configuration) =>
            {
                var origins = configuration.GetSection(AllowedOriginsKey).Get<string[]>() ?? [];
                options.AddDefaultPolicy(policy => policy
                    .WithOrigins(origins.Where(o => !string.IsNullOrWhiteSpace(o)).ToArray())
                    .AllowAnyHeader()
                    .AllowAnyMethod());
            });

        return services;
    }

    public static IApplicationBuilder UseGoalFlowCors(this IApplicationBuilder app) => app.UseCors();
}
