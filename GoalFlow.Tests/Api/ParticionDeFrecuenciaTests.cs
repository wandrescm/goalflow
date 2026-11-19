using GoalFlow.Api.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace GoalFlow.Tests.Api;

/// <summary>Spec 010 T-10 y T-11: función pura de partición, sobre <see cref="DefaultHttpContext"/>, sin host.</summary>
public class ParticionDeFrecuenciaTests
{
    public static TheoryData<string[], string> ClavesDelCliente => new()
    {
        { ["203.0.113.7"], "203.0.113.7" },
        { ["::ffff:203.0.113.7"], "203.0.113.7" },
        { ["2001:db8:1:2:aaaa::1"], "2001:db8:1:2::" },
        { ["2001:db8:1:2:bbbb::9"], "2001:db8:1:2::" },
        { ["2001:db8:1:3::1"], "2001:db8:1:3::" },
        { [], "sin-ip" },
        { ["no-es-ip"], "sin-ip" },
        { ["203.0.113.7", "203.0.113.8"], "sin-ip" },
        { ["  203.0.113.7  "], "203.0.113.7" },
    };

    [Theory]
    [MemberData(nameof(ClavesDelCliente))]
    public void T10_la_clave_del_cliente_normaliza_el_valor_de_CF_Connecting_IP(string[] instancias, string esperada)
    {
        var contexto = new DefaultHttpContext();
        if (instancias.Length > 0) contexto.Request.Headers["CF-Connecting-IP"] = new StringValues(instancias);

        Assert.Equal(esperada, ParticionDeFrecuencia.ClaveDelCliente(contexto.Request));
    }

    [Fact]
    public void T10_dos_IPv6_del_mismo_64_comparten_clave_y_otro_64_tiene_otra()
    {
        string Clave(string ip)
        {
            var contexto = new DefaultHttpContext();
            contexto.Request.Headers["CF-Connecting-IP"] = ip;
            return ParticionDeFrecuencia.ClaveDelCliente(contexto.Request);
        }

        Assert.Equal(Clave("2001:db8:1:2:aaaa::1"), Clave("2001:db8:1:2:bbbb::9"));
        Assert.NotEqual(Clave("2001:db8:1:2:aaaa::1"), Clave("2001:db8:1:3::1"));
    }

    [Theory]
    [InlineData("/health", ClaseDeRuta.Salud)]
    [InlineData("/HEALTH", ClaseDeRuta.Salud)]
    [InlineData("/liga/admin/sesion", ClaseDeRuta.Admin)]
    [InlineData("/liga/ADMIN/sesion", ClaseDeRuta.Admin)]
    [InlineData("/liga/admin", ClaseDeRuta.Admin)]
    [InlineData("/liga/equipos", ClaseDeRuta.Publica)]
    [InlineData("/no/existe/ruta", ClaseDeRuta.Publica)]
    [InlineData("/", ClaseDeRuta.Publica)]
    public void T11_cada_ruta_pertenece_a_exactamente_una_clase(string ruta, ClaseDeRuta esperada)
    {
        Assert.Equal(esperada, ParticionDeFrecuencia.ClaseDe(new PathString(ruta)));
    }
}
