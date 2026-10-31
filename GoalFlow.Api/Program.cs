using GoalFlow.Api.Hosting;
using GoalFlow.Infrastructure.Persistencia;

var builder = WebApplication.CreateBuilder(args);

// Render injects PORT; without it we leave the URL alone (the aspnet image
// listens on 8080 and launchSettings drives local runs). Plain HTTP only (ADR-05 2.1, 2.4).
if (Environment.GetEnvironmentVariable("PORT") is { Length: > 0 } port)
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}


builder.Services.AddGoalFlowForwardedHeaders();
builder.Services.AddGoalFlowCors();
builder.Services.AddGoalFlowPersistencia();

var app = builder.Build();

app.UseGoalFlowForwardedHeaders();
app.UseGoalFlowCors();

app.MapGet("/health", () => Results.Ok());
app.MapGoalFlowEquipos();

app.Run();

public partial class Program { }
