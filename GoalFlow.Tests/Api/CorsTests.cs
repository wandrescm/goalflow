using Microsoft.AspNetCore.Mvc.Testing;

namespace GoalFlow.Tests.Api;

public class CorsTests
{
    private const string Allowed = "https://app.goalflow.example";

    private static async Task<HttpResponseMessage> PreflightAsync(string? configuredOrigin, string requestOrigin)
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            if (configuredOrigin is not null)
            {
                builder.UseSetting("Cors:AllowedOrigins:0", configuredOrigin);
            }
        });
        var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Options, "/health");
        request.Headers.Add("Origin", requestOrigin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task Cors_AllowsConfiguredOrigin()
    {
        using var response = await PreflightAsync(Allowed, Allowed);

        Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values));
        Assert.Equal(Allowed, Assert.Single(values));
    }

    [Fact]
    public async Task Cors_RejectsOtherOrigin()
    {
        using var response = await PreflightAsync(Allowed, "https://evil.example");

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Cors_RejectsAll_WhenNotConfigured()
    {
        using var response = await PreflightAsync(null, Allowed);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }
}
