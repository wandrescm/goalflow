using GoalFlow.Application.Tenancy;
using GoalFlow.Domain.Tenancy;
using GoalFlow.Infrastructure.Persistencia;

namespace GoalFlow.Tests.Tenancy;

/// <summary>Spec 002 T-04.</summary>
public class ContextoDeTenantTests
{
    private static readonly TenantId LigaA = new(Guid.NewGuid());
    private static readonly TenantId LigaB = new(Guid.NewGuid());

    [Fact]
    public void Leer_sin_fijar_lanza_excepcion()
    {
        var contexto = new ContextoDeTenant();

        Assert.False(contexto.TieneTenant);
        Assert.Throws<TenantAusenteException>(() => contexto.Current);
    }

    [Fact]
    public void Fijar_un_valor_distinto_lanza_excepcion()
    {
        var contexto = new ContextoDeTenant();
        contexto.Establecer(LigaA);

        Assert.Throws<InvalidOperationException>(() => contexto.Establecer(LigaB));
        Assert.Equal(LigaA, contexto.Current);
    }

    [Fact]
    public void Fijar_el_mismo_valor_dos_veces_esta_permitido()
    {
        var contexto = new ContextoDeTenant();
        contexto.Establecer(LigaA);

        contexto.Establecer(LigaA);

        Assert.True(contexto.TieneTenant);
        Assert.Equal(LigaA, contexto.Current);
    }

    [Fact]
    public void Fijar_un_tenant_vacio_lanza_excepcion()
    {
        var contexto = new ContextoDeTenant();

        Assert.Throws<ArgumentException>(() => contexto.Establecer(default));
        Assert.False(contexto.TieneTenant);
    }
}
