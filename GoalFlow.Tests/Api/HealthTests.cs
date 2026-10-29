using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace GoalFlow.Tests.Api;

public class HealthTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Health_ReturnsOk_WithoutDatabase()
    {
        // No connection string is configured anywhere in this factory.
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
