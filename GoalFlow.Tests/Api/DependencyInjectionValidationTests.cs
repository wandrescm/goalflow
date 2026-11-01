using System.Net;
using GoalFlow.Application.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace GoalFlow.Tests.Api;

/// <summary>Spec 002 T-24: outside Development, ASP.NET Core validates nothing unless Program.cs asks for it.</summary>
public class DependencyInjectionValidationTests(ITestOutputHelper output)
{
    private sealed class SingletonDePrueba(ITenantContext tenantContext)
    {
        public ITenantContext TenantContext { get; } = tenantContext;
    }

    private static IEnumerable<Exception> Cadena(Exception? exception)
    {
        if (exception is null) yield break;

        yield return exception;
        var inner = exception is AggregateException aggregate ? aggregate.InnerExceptions : [exception.InnerException!];
        foreach (var nested in inner.SelectMany(Cadena)) yield return nested;
    }

    [Fact]
    public async Task T24_Production_host_with_correct_registrations_starts()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task T24_Production_host_fails_to_start_when_a_singleton_depends_on_a_scoped_service()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureTestServices(services => services.AddSingleton<SingletonDePrueba>());
        });

        var exception = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(exception);
        var chain = Cadena(exception).ToList();
        foreach (var link in chain) output.WriteLine($"{link.GetType().FullName}: {link.Message}");

        Assert.Contains(chain, e => e is InvalidOperationException
            && e.Message.Contains(nameof(SingletonDePrueba))
            && e.Message.Contains(nameof(ITenantContext)));
    }
}
