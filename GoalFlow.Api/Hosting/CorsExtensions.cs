namespace GoalFlow.Api.Hosting;

public static class CorsExtensions
{
    public static IServiceCollection AddGoalFlowCors(this IServiceCollection services) => services;

    public static IApplicationBuilder UseGoalFlowCors(this IApplicationBuilder app) => app;
}
