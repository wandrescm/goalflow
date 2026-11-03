using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using static GoalFlow.Tests.Isolation.BaseDeAislamiento;

namespace GoalFlow.Tests.Isolation;

/// <summary>Spec 003: conectada como goalflow_app, bajo RLS. T-11 se demuestra con la mutación de T-01.</summary>
[Collection(ColeccionDeAislamiento.Nombre)]
public class SesionDeAdminTests(BaseDeAislamiento db)
{
    private const string RutaA = "/liga-a/admin/sesion";

    private readonly LlaveDePrueba _k1 = new();

    private WebApplicationFactory<Program> Anfitrion() => AnfitrionDeAdmin.Crear(db, new FuenteDeJwksDePrueba(_k1));

    private static Task<HttpResponseMessage> Pedir(HttpClient cliente, string ruta, string? token = null)
    {
        var peticion = new HttpRequestMessage(HttpMethod.Get, ruta);
        if (token is not null) peticion.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return cliente.SendAsync(peticion);
    }

    private async Task<HttpStatusCode> EstadoCon(string token)
    {
        await using var fabrica = Anfitrion();
        return (await Pedir(fabrica.CreateClient(), RutaA, token)).StatusCode;
    }

    private static long EnSegundos(TimeSpan desdeAhora) => DateTimeOffset.UtcNow.Add(desdeAhora).ToUnixTimeSeconds();

    [Fact]
    public async Task T01_token_valido_de_Admin_devuelve_200_con_slug_y_rol()
    {
        await using var fabrica = Anfitrion();

        var respuesta = await Pedir(fabrica.CreateClient(), RutaA, TokensDePrueba.Firmar(_k1, UsuarioU1));

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("liga-a", cuerpo.GetProperty("slug").GetString());
        Assert.Equal("admin", cuerpo.GetProperty("rol").GetString());
    }

    [Fact]
    public async Task T02_sin_token_devuelve_401()
    {
        await using var fabrica = Anfitrion();

        var cliente = fabrica.CreateClient();
        var vencido = TokensDePrueba.Firmar(_k1, UsuarioU1, c => c["exp"] = EnSegundos(TimeSpan.FromMinutes(-2)));

        foreach (var respuesta in new[] { await Pedir(cliente, RutaA), await Pedir(cliente, RutaA, vencido) })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
            Assert.Equal("Bearer", respuesta.Headers.WwwAuthenticate.ToString());
            Assert.Empty(await respuesta.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task T03_firma_de_otra_llave_con_el_mismo_kid_devuelve_401()
    {
        var atacante = new LlaveDePrueba(_k1.Kid);

        Assert.Equal(HttpStatusCode.Unauthorized, await EstadoCon(TokensDePrueba.Firmar(atacante, UsuarioU1)));
    }

    [Fact]
    public async Task T04_HS256_con_la_llave_publica_como_secreto_devuelve_401()
    {
        var secreto = new SymmetricSecurityKey(_k1.BytesPublicos) { KeyId = _k1.Kid };
        var credenciales = new SigningCredentials(secreto, SecurityAlgorithms.HmacSha256);

        Assert.Equal(HttpStatusCode.Unauthorized, await EstadoCon(TokensDePrueba.Firmar(credenciales, UsuarioU1)));
    }

    [Fact]
    public async Task T04_alg_none_devuelve_401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, await EstadoCon(TokensDePrueba.SinFirma(_k1.Kid, UsuarioU1)));
    }

    [Fact]
    public async Task T04_RS256_bien_firmado_con_una_llave_publicada_devuelve_401()
    {
        using var rsa = RSA.Create(2048);
        const string kid = "k3";
        var parametros = rsa.ExportParameters(includePrivateParameters: false);
        var fuente = new FuenteDeJwksDePrueba(_k1);
        fuente.AgregarJwk(new
        {
            kty = "RSA",
            alg = "RS256",
            use = "sig",
            kid,
            n = Base64UrlEncoder.Encode(parametros.Modulus),
            e = Base64UrlEncoder.Encode(parametros.Exponent),
        });
        var credenciales = new SigningCredentials(new RsaSecurityKey(rsa) { KeyId = kid }, SecurityAlgorithms.RsaSha256);
        await using var fabrica = AnfitrionDeAdmin.Crear(db, fuente);

        var respuesta = await Pedir(fabrica.CreateClient(), RutaA, TokensDePrueba.Firmar(credenciales, UsuarioU1));

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [Fact]
    public async Task T04_las_opciones_del_registro_real_solo_admiten_ES256()
    {
        await using var fabrica = Anfitrion();
        var opciones = fabrica.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get("Supabase");

        Assert.Equal(["ES256"], opciones.TokenValidationParameters.ValidAlgorithms);
    }

    public static TheoryData<string, object> ClaimsInvalidos => new()
    {
        { "iss", "https://otro-emisor.invalid/auth/v1" },
        { "aud", "anon" },
        { "role", "anon" },
        { "is_anonymous", true },
        { "sub", "no-es-un-uuid" },
    };

    [Theory]
    [MemberData(nameof(ClaimsInvalidos))]
    public async Task T05_claim_invalido_devuelve_401(string claim, object valor)
    {
        var token = TokensDePrueba.Firmar(_k1, UsuarioU1, c => c[claim] = valor);

        Assert.Equal(HttpStatusCode.Unauthorized, await EstadoCon(token));
    }

    [Fact]
    public async Task T05_T06_vencido_hace_2_minutos_devuelve_401()
    {
        var token = TokensDePrueba.Firmar(_k1, UsuarioU1, c => c["exp"] = EnSegundos(TimeSpan.FromMinutes(-2)));

        Assert.Equal(HttpStatusCode.Unauthorized, await EstadoCon(token));
    }

    [Fact]
    public async Task T06_vencido_hace_10_segundos_esta_dentro_del_margen()
    {
        var token = TokensDePrueba.Firmar(_k1, UsuarioU1, c => c["exp"] = EnSegundos(TimeSpan.FromSeconds(-10)));

        Assert.Equal(HttpStatusCode.OK, await EstadoCon(token));
    }

    [Fact]
    public async Task T03_nbf_futuro_devuelve_401()
    {
        var token = TokensDePrueba.Firmar(_k1, UsuarioU1, c => c["nbf"] = EnSegundos(TimeSpan.FromMinutes(5)));

        Assert.Equal(HttpStatusCode.Unauthorized, await EstadoCon(token));
    }

    [Fact]
    public async Task T07_caso1_una_llave_rotada_se_acepta_tras_un_refresco()
    {
        var fuente = new FuenteDeJwksDePrueba(_k1);
        await using var fabrica = AnfitrionDeAdmin.Crear(db, fuente);
        var cliente = fabrica.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await Pedir(cliente, RutaA, TokensDePrueba.Firmar(_k1, UsuarioU1))).StatusCode);
        var lecturas = fuente.Lecturas;

        var k2 = new LlaveDePrueba();
        fuente.Agregar(k2);
        var respuesta = await Pedir(cliente, RutaA, TokensDePrueba.Firmar(k2, UsuarioU1));

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal(lecturas + 1, fuente.Lecturas);
    }

    [Fact]
    public async Task T07_caso2_diez_peticiones_no_vuelven_a_leer_la_fuente()
    {
        var fuente = new FuenteDeJwksDePrueba(_k1);
        await using var fabrica = AnfitrionDeAdmin.Crear(db, fuente);
        var cliente = fabrica.CreateClient();
        var token = TokensDePrueba.Firmar(_k1, UsuarioU1);
        Assert.Equal(HttpStatusCode.OK, (await Pedir(cliente, RutaA, token)).StatusCode);
        var lecturas = fuente.Lecturas;

        for (var i = 0; i < 10; i++)
            Assert.Equal(HttpStatusCode.OK, (await Pedir(cliente, RutaA, token)).StatusCode);

        Assert.Equal(lecturas, fuente.Lecturas);
    }

    [Fact]
    public async Task T07_caso3_diez_kid_desconocidos_leen_la_fuente_como_mucho_una_vez()
    {
        var fuente = new FuenteDeJwksDePrueba(_k1);
        await using var fabrica = AnfitrionDeAdmin.Crear(db, fuente);
        var cliente = fabrica.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await Pedir(cliente, RutaA, TokensDePrueba.Firmar(_k1, UsuarioU1))).StatusCode);
        var lecturas = fuente.Lecturas;

        // Firmados con K1 a propósito: el kid debe coincidir, no basta con que alguna llave verifique (R-01).
        for (var i = 0; i < 10; i++)
        {
            var token = TokensDePrueba.Firmar(_k1.ConKid(Guid.NewGuid().ToString("N")), UsuarioU1);
            Assert.Equal(HttpStatusCode.Unauthorized, (await Pedir(cliente, RutaA, token)).StatusCode);
        }

        Assert.InRange(fuente.Lecturas, lecturas, lecturas + 1);
    }

    [Fact]
    public async Task T08_Admin_de_otra_liga_devuelve_403()
    {
        await using var fabrica = Anfitrion();

        var respuesta = await Pedir(fabrica.CreateClient(), "/liga-b/admin/sesion", TokensDePrueba.Firmar(_k1, UsuarioU1));

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }

    [Fact]
    public async Task T09_membresia_borrada_devuelve_403_con_el_mismo_token()
    {
        var usuario = Guid.NewGuid();
        await db.EjecutarComoSuperusuario(
            $"insert into goalflow.admins_liga (user_id, tenant_id, rol) values ('{usuario}', '{LigaA.Value}', 'admin')");
        var borrar = $"delete from goalflow.admins_liga where user_id = '{usuario}'";

        try
        {
            await using var fabrica = Anfitrion();
            var cliente = fabrica.CreateClient();
            var token = TokensDePrueba.Firmar(_k1, usuario);
            Assert.Equal(HttpStatusCode.OK, (await Pedir(cliente, RutaA, token)).StatusCode);

            await db.EjecutarComoSuperusuario(borrar);

            Assert.Equal(HttpStatusCode.Forbidden, (await Pedir(cliente, RutaA, token)).StatusCode);
        }
        finally
        {
            await db.EjecutarComoSuperusuario(borrar);
        }
    }

    [Fact]
    public async Task T10_slug_inexistente_devuelve_404_con_y_sin_token()
    {
        await using var fabrica = Anfitrion();
        var cliente = fabrica.CreateClient();
        const string ruta = "/no-existe/admin/sesion";

        Assert.Equal(HttpStatusCode.NotFound, (await Pedir(cliente, ruta)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Pedir(cliente, ruta, TokensDePrueba.Firmar(_k1, UsuarioU1))).StatusCode);
    }

    [Fact]
    public async Task T12_ninguna_entrada_de_log_contiene_el_token_ni_Bearer()
    {
        var logs = new CapturaDeLogs();
        await using var fabrica = AnfitrionDeAdmin.Crear(db, new FuenteDeJwksDePrueba(_k1), logs);
        var cliente = fabrica.CreateClient();
        var valido = TokensDePrueba.Firmar(_k1, UsuarioU1);
        var vencido = TokensDePrueba.Firmar(_k1, UsuarioU1, c => c["exp"] = EnSegundos(TimeSpan.FromMinutes(-2)));

        Assert.Equal(HttpStatusCode.OK, (await Pedir(cliente, RutaA, valido)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Pedir(cliente, RutaA, vencido)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Pedir(cliente, "/liga-b/admin/sesion", valido)).StatusCode);

        Assert.NotEmpty(logs.Entradas);
        Assert.All(logs.Entradas, entrada =>
        {
            Assert.DoesNotContain(valido, entrada);
            Assert.DoesNotContain(vencido, entrada);
            Assert.DoesNotContain(valido.Split('.')[1], entrada);
            Assert.DoesNotContain("Bearer", entrada);
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task T13_sin_JWKS_la_ruta_de_Admin_responde_401_y_las_publicas_siguen(bool fuenteCaida)
    {
        var logs = new CapturaDeLogs();
        var fuente = new FuenteDeJwksDePrueba(_k1)
        {
            Documento = fuenteCaida ? () => throw new HttpRequestException("JWKS no disponible") : () => "esto no es un JWKS",
        };
        await using var fabrica = AnfitrionDeAdmin.Crear(db, fuente, logs);
        var cliente = fabrica.CreateClient();
        var token = TokensDePrueba.Firmar(_k1, UsuarioU1);

        var respuesta = await Pedir(cliente, RutaA, token);

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        Assert.Equal("Bearer", respuesta.Headers.WwwAuthenticate.ToString());
        Assert.NotEmpty(logs.Advertencias);
        Assert.All(logs.Entradas, entrada =>
        {
            Assert.DoesNotContain(token, entrada);
            Assert.DoesNotContain(token.Split('.')[1], entrada);
        });
        Assert.All(logs.Advertencias, entrada => Assert.DoesNotContain("Bearer", entrada));
        Assert.Equal(HttpStatusCode.OK, (await Pedir(cliente, "/liga-a/equipos")).StatusCode);
    }

    [Fact]
    public async Task Handoff_una_ruta_publica_ignora_el_token_y_no_lee_el_JWKS()
    {
        var fuente = new FuenteDeJwksDePrueba(_k1);
        await using var fabrica = AnfitrionDeAdmin.Crear(db, fuente);

        var respuesta = await Pedir(fabrica.CreateClient(), "/liga-a/equipos", "esto-no-es-un-token");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal(0, fuente.Lecturas);
    }

    [Fact]
    public async Task Handoff_sin_configuracion_de_Auth_solo_fallan_las_rutas_de_Admin()
    {
        var logs = new CapturaDeLogs();
        await using var fabrica = AnfitrionDeAdmin.Crear(db, new FuenteDeJwksDePrueba(_k1), logs, conAuth: false);
        var cliente = fabrica.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await Pedir(cliente, "/health", "esto-no-es-un-token")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Pedir(cliente, "/liga-a/equipos", "esto-no-es-un-token")).StatusCode);
        var respuesta = await Pedir(cliente, RutaA, TokensDePrueba.Firmar(_k1, UsuarioU1));

        Assert.Equal(HttpStatusCode.InternalServerError, respuesta.StatusCode);
        Assert.Contains(logs.Entradas, e => e.Contains("Missing configuration value Auth:"));
    }
}
