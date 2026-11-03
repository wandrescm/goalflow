using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;
using static GoalFlow.Tests.Isolation.BaseDeAislamiento;

namespace GoalFlow.Tests.Isolation;

/// <summary>Spec 003 T-15: la API arranca con el JWKS caído y sin llaves en caché.</summary>
[Collection(ColeccionDeAislamiento.Nombre)]
public sealed class EsperaDeJwksTests : IAsyncDisposable
{
    private const string RutaA = "/liga-a/admin/sesion";

    private static readonly TimeSpan PasadaLaEspera = AnfitrionDeAdmin.EsperaTrasFallo + TimeSpan.FromSeconds(1);

    private readonly LlaveDePrueba _k1 = new();
    private readonly FuenteDeJwksDePrueba _fuente;
    private readonly CapturaDeLogs _logs = new();
    private readonly RelojDePrueba _reloj = new();
    private readonly WebApplicationFactory<Program> _fabrica;
    private readonly HttpClient _cliente;
    private readonly string _token;

    public EsperaDeJwksTests(BaseDeAislamiento db)
    {
        _fuente = new FuenteDeJwksDePrueba(_k1) { Documento = () => throw new HttpRequestException("JWKS no disponible") };
        _fabrica = AnfitrionDeAdmin.Crear(db, _fuente, _logs, reloj: _reloj);
        _cliente = _fabrica.CreateClient();
        _token = TokensDePrueba.Firmar(_k1, UsuarioU1);
    }

    public ValueTask DisposeAsync() => _fabrica.DisposeAsync();

    private async Task<HttpStatusCode> Pedir(string token)
    {
        var peticion = new HttpRequestMessage(HttpMethod.Get, RutaA);
        peticion.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (await _cliente.SendAsync(peticion)).StatusCode;
    }

    [Fact]
    public async Task T15_caso1_cinco_peticiones_dentro_de_la_espera_leen_una_vez_y_avisan_una_vez()
    {
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, await Pedir(_token));

        Assert.Equal(1, _fuente.Lecturas);
        Assert.Single(_logs.Advertencias);
    }

    [Fact]
    public async Task T15_caso2_pasada_la_espera_y_con_la_fuente_sana_responde_200_tras_una_lectura()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, await Pedir(_token));

        _fuente.Documento = null;
        _reloj.Avanzar(PasadaLaEspera);

        Assert.Equal(HttpStatusCode.OK, await Pedir(_token));
        Assert.Equal(2, _fuente.Lecturas);
    }

    [Fact]
    public async Task T15_caso3_un_JWT_falso_dentro_de_la_espera_no_lee_la_fuente()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, await Pedir(_token));

        var falso = TokensDePrueba.Firmar(new LlaveDePrueba(), UsuarioU1);

        Assert.Equal(HttpStatusCode.Unauthorized, await Pedir(falso));
        Assert.Equal(1, _fuente.Lecturas);
    }

    [Fact]
    public async Task T15_si_la_fuente_sigue_caida_reintenta_y_avisa_una_vez_por_periodo()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, await Pedir(_token));

        _reloj.Avanzar(PasadaLaEspera);

        Assert.Equal(HttpStatusCode.Unauthorized, await Pedir(_token));
        Assert.Equal(HttpStatusCode.Unauthorized, await Pedir(_token));
        Assert.Equal(2, _fuente.Lecturas);
        Assert.Equal(2, _logs.Advertencias.Count);
    }

    [Fact]
    public async Task T15_C11_con_llaves_en_cache_un_refresco_fallido_las_conserva()
    {
        _fuente.Documento = null;
        Assert.Equal(HttpStatusCode.OK, await Pedir(_token));

        _fuente.Documento = () => throw new HttpRequestException("JWKS no disponible");

        Assert.Equal(HttpStatusCode.Unauthorized, await Pedir(TokensDePrueba.Firmar(new LlaveDePrueba(), UsuarioU1)));
        Assert.Equal(HttpStatusCode.OK, await Pedir(_token));
    }
}
