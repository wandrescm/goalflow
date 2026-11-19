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

    private sealed record Seen(string Scheme, string RemoteIp, string ForwardedFor);

    private static async Task<string> SendAsync(string environment, params (string Name, string Value)[] headers)
    {
        var seen = await SeenAsync(environment, headers);
        return $"{seen.Scheme}|{seen.RemoteIp}";
    }

    /// <summary>What the endpoint sees after the forwarded-headers middleware: scheme, connection address and the X-Forwarded-For value.</summary>
    private static async Task<Seen> SeenAsync(string environment, params (string Name, string Value)[] headers)
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
                        $"{context.Request.Scheme}|{context.Connection.RemoteIpAddress}|{context.Request.Headers["X-Forwarded-For"]}"));
                }))
            .StartAsync();

        var client = host.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        foreach (var (name, value) in headers)
        {
            request.Headers.Add(name, value);
        }

        using var response = await client.SendAsync(request);
        var parts = (await response.Content.ReadAsStringAsync()).Split('|', 3);
        return new Seen(parts[0], parts[1], parts[2]);
    }

    [Fact]
    public async Task ForwardedProto_SetsHttpsScheme()
    {
        var result = await SendAsync(Environments.Production, ("X-Forwarded-Proto", "https"));

        Assert.StartsWith("https|", result);
    }

    // Spec 010 R-23 / T-20: X-Forwarded-For no se aplica a la dirección de la conexión (reemplaza a ForwardedFor_SetsClientIp).
    [Fact]
    public async Task ForwardedFor_IsNotAppliedToTheConnectionAddress()
    {
        var result = await SendAsync(Environments.Production, ("X-Forwarded-For", "203.0.113.7"));

        Assert.EndsWith("|10.0.0.1", result);
    }

    // Spec 010 R-23 / T-20: ninguna entrada de varias se aplica (reemplaza a ForwardedFor_IgnoresClientInjectedValue).
    [Fact]
    public async Task ForwardedFor_WithSeveralEntries_AppliesNone()
    {
        var result = await SendAsync(Environments.Production, ("X-Forwarded-For", "198.51.100.1, 203.0.113.7"));

        Assert.EndsWith("|10.0.0.1", result);
    }

    // Spec 010 T-20: la forma capturada en producción (§3.2.1): https, conexión intacta y X-Forwarded-For sin recortar.
    [Fact]
    public async Task CapturedProductionShape_KeepsHttpsAndLeavesTheAddressAndTheHeaderIntact()
    {
        const string chain = "198.51.100.10, 104.22.17.40, 10.200.0.5";

        var seen = await SeenAsync(
            Environments.Production,
            ("X-Forwarded-For", chain),
            ("CF-Connecting-IP", "198.51.100.10"),
            ("True-Client-IP", "198.51.100.10"),
            ("X-Forwarded-Proto", "https"));

        Assert.Equal("https", seen.Scheme);
        Assert.Equal("10.0.0.1", seen.RemoteIp);
        Assert.Equal(chain, seen.ForwardedFor);
    }

    // Spec 010 T-20: en Development el esquema sigue como hoy (el proxy 10.0.0.1 no es de confianza).
    [Fact]
    public async Task CapturedShape_Development_KeepsHttpScheme()
    {
        var seen = await SeenAsync(Environments.Development, ("X-Forwarded-Proto", "https"));

        Assert.Equal("http", seen.Scheme);
        Assert.Equal("10.0.0.1", seen.RemoteIp);
    }

    [Fact]
    public async Task ForwardedHeaders_Development_IgnoresNonLoopbackProxy()
    {
        var result = await SendAsync(Environments.Development, ("X-Forwarded-For", "203.0.113.7"));

        Assert.EndsWith("|10.0.0.1", result);
    }
}
