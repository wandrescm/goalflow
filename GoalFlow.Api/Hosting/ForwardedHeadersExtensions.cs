namespace GoalFlow.Api.Hosting;

public static class ForwardedHeadersExtensions
{
    public static IServiceCollection AddGoalFlowForwardedHeaders(this IServiceCollection services) => services;

    public static IApplicationBuilder UseGoalFlowForwardedHeaders(this IApplicationBuilder app) => app;
}
