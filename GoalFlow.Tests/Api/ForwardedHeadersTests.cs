using System.Net;
using GoalFlow.Api.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GoalFlow.Tests.Api;

public class ForwardedHeadersTests
{
    // Simulates Render's proxy: it is not on loopback.
    private static readonly IPAddress ProxyAddress = IPAddress.Parse("10.0.0.1");

    private static async Task<string> SendAsync(string environment, params (string Name, string Value)[] headers)
    {
        using var host = await new HostBuilder()
            .UseEnvironment(environment)
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services => services.AddGoalFlowForwardedHeaders())
                .Configure(app =>
                {
                    app.Use((context, next) =>
                    {
                        context.Connection.RemoteIpAddress = ProxyAddress;
                        return next(context);
                    });
                    app.UseGoalFlowForwardedHeaders();
                    app.Run(context => context.Response.WriteAsync(
                        $"{context.Request.Scheme}|{context.Connection.RemoteIpAddress}"));
                }))
            .StartAsync();

        var client = host.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        foreach (var (name, value) in headers)
        {
            request.Headers.Add(name, value);
        }

        using var response = await client.SendAsync(request);
        return await response.Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task ForwardedProto_SetsHttpsScheme()
    {
        var result = await SendAsync(Environments.Production, ("X-Forwarded-Proto", "https"));

        Assert.StartsWith("https|", result);
    }

    [Fact]
    public async Task ForwardedFor_SetsClientIp()
    {
        var result = await SendAsync(Environments.Production, ("X-Forwarded-For", "203.0.113.7"));

        Assert.EndsWith("|203.0.113.7", result);
    }

    [Fact]
    public async Task ForwardedFor_IgnoresClientInjectedValue()
    {
        var result = await SendAsync(Environments.Production, ("X-Forwarded-For", "198.51.100.1, 203.0.113.7"));

        Assert.EndsWith("|203.0.113.7", result);
    }

    [Fact]
    public async Task ForwardedHeaders_Development_IgnoresNonLoopbackProxy()
    {
        var result = await SendAsync(Environments.Development, ("X-Forwarded-For", "203.0.113.7"));

        Assert.EndsWith("|10.0.0.1", result);
    }
}
