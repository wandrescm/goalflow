using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Tokens;

namespace GoalFlow.Tests.Isolation;

/// <summary>Par P-256 en memoria: nunca se escribe a disco (Spec 003 §4).</summary>
public sealed class LlaveDePrueba
{
    private readonly ECDsa _ecdsa;

    public LlaveDePrueba(string? kid = null) : this(ECDsa.Create(ECCurve.NamedCurves.nistP256), kid ?? Guid.NewGuid().ToString("N"))
    {
    }

    private LlaveDePrueba(ECDsa ecdsa, string kid)
    {
        _ecdsa = ecdsa;
        Kid = kid;
    }

    public string Kid { get; }

    public byte[] BytesPublicos => _ecdsa.ExportSubjectPublicKeyInfo();

    public SigningCredentials Credenciales =>
        new(new ECDsaSecurityKey(_ecdsa) { KeyId = Kid }, SecurityAlgorithms.EcdsaSha256);

    /// <summary>La misma llave con otro kid en el encabezado del token.</summary>
    public LlaveDePrueba ConKid(string kid) => new(_ecdsa, kid);

    public object JwkPublica()
    {
        var parametros = _ecdsa.ExportParameters(includePrivateParameters: false);
        return new
        {
            kty = "EC",
            crv = "P-256",
            alg = "ES256",
            use = "sig",
            kid = Kid,
            x = Base64UrlEncoder.Encode(parametros.Q.X),
            y = Base64UrlEncoder.Encode(parametros.Q.Y),
        };
    }
}

/// <summary>Reemplaza la descarga del JWKS y cuenta las lecturas (Spec 003 C-05, T-07).</summary>
public sealed class FuenteDeJwksDePrueba(params LlaveDePrueba[] iniciales) : IDocumentRetriever
{
    private readonly List<LlaveDePrueba> _llaves = [.. iniciales];
    private readonly List<object> _otrasJwk = [];
    private int _lecturas;

    public int Lecturas => Volatile.Read(ref _lecturas);

    /// <summary>Si se fija, reemplaza el documento: para simular una fuente caída o inválida (T-13).</summary>
    public Func<string>? Documento { get; set; }

    public void Agregar(LlaveDePrueba llave)
    {
        lock (_llaves) _llaves.Add(llave);
    }

    public void AgregarJwk(object jwk)
    {
        lock (_llaves) _otrasJwk.Add(jwk);
    }

    public Task<string> GetDocumentAsync(string address, CancellationToken cancel)
    {
        Interlocked.Increment(ref _lecturas);
        if (Documento is not null) return Task.FromResult(Documento());

        lock (_llaves)
            return Task.FromResult(JsonSerializer.Serialize(new { keys = _llaves.Select(l => l.JwkPublica()).Concat(_otrasJwk) }));
    }
}

public static class TokensDePrueba
{
    public const string Emisor = "https://proyecto-de-prueba.invalid/auth/v1";

    private static readonly JsonWebTokenHandler Manejador = new() { SetDefaultTimesOnTokenCreation = false };

    public static Dictionary<string, object> ClaimsValidos(Guid usuario)
    {
        var ahora = DateTimeOffset.UtcNow;
        return new Dictionary<string, object>
        {
            ["iss"] = Emisor,
            ["aud"] = "authenticated",
            ["sub"] = usuario.ToString(),
            ["role"] = "authenticated",
            ["is_anonymous"] = false,
            ["iat"] = ahora.AddMinutes(-5).ToUnixTimeSeconds(),
            ["exp"] = ahora.AddMinutes(30).ToUnixTimeSeconds(),
        };
    }

    public static string Firmar(SigningCredentials credenciales, Guid usuario, Action<Dictionary<string, object>>? ajustar = null)
    {
        var claims = ClaimsValidos(usuario);
        ajustar?.Invoke(claims);
        return Manejador.CreateToken(new SecurityTokenDescriptor { Claims = claims, SigningCredentials = credenciales });
    }

    public static string Firmar(LlaveDePrueba llave, Guid usuario, Action<Dictionary<string, object>>? ajustar = null) =>
        Firmar(llave.Credenciales, usuario, ajustar);

    public static string SinFirma(string kid, Guid usuario)
    {
        static string Parte(object valor) => Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(valor)));

        return $"{Parte(new { alg = "none", typ = "JWT", kid })}.{Parte(ClaimsValidos(usuario))}.";
    }
}

/// <summary>Logger que guarda todo lo que la API escribe, excepciones incluidas (Spec 003 T-12).</summary>
public sealed class CapturaDeLogs : ILoggerProvider
{
    private readonly List<(LogLevel Nivel, string Texto)> _entradas = [];

    public IReadOnlyList<string> Entradas
    {
        get
        {
            lock (_entradas) return [.. _entradas.Select(e => e.Texto)];
        }
    }

    public IReadOnlyList<string> Advertencias
    {
        get
        {
            lock (_entradas) return [.. _entradas.Where(e => e.Nivel >= LogLevel.Warning).Select(e => e.Texto)];
        }
    }

    public ILogger CreateLogger(string categoryName) => new Registrador(this);

    public void Dispose()
    {
    }

    private sealed class Registrador(CapturaDeLogs captura) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (captura._entradas) captura._entradas.Add((logLevel, $"{formatter(state, exception)} {exception}"));
        }
    }
}

/// <summary>La API real contra el contenedor, con el JWKS en memoria. Ninguna prueba llama a Supabase.</summary>
public static class AnfitrionDeAdmin
{
    public static WebApplicationFactory<Program> Crear(
        BaseDeAislamiento db, FuenteDeJwksDePrueba fuente, CapturaDeLogs? logs = null, bool conAuth = true)
    {
        var ajustes = new Dictionary<string, string?> { ["ConnectionStrings:GoalFlow"] = db.CadenaApp };
        if (conAuth)
        {
            ajustes["Auth:Issuer"] = TokensDePrueba.Emisor;
            ajustes["Auth:JwksUrl"] = "https://proyecto-de-prueba.invalid/auth/v1/.well-known/jwks.json";
        }
        else
        {
            ajustes["Auth:Issuer"] = null;
            ajustes["Auth:JwksUrl"] = null;
        }

        return new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.ConfigureAppConfiguration((_, configuracion) => configuracion.AddInMemoryCollection(ajustes));
            b.ConfigureTestServices(servicios =>
            {
                servicios.RemoveAll<IDocumentRetriever>();
                servicios.AddSingleton<IDocumentRetriever>(fuente);
            });

            if (logs is not null)
            {
                b.ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddProvider(logs);
                    logging.AddFilter<CapturaDeLogs>(null, LogLevel.Trace);
                });
            }
        });
    }
}
