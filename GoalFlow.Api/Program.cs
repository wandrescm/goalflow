using GoalFlow.Api.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Render injects PORT; the container serves plain HTTP only (ADR-05 2.1, 2.4).
builder.WebHost.UseUrls($"http://0.0.0.0:{Environment.GetEnvironmentVariable("PORT") ?? "8080"}");

builder.Services.AddGoalFlowForwardedHeaders();
builder.Services.AddGoalFlowCors();

var app = builder.Build();

app.UseGoalFlowForwardedHeaders();
app.UseGoalFlowCors();

app.MapGet("/health", () => Results.Ok());

app.Run();

public partial class Program { }
