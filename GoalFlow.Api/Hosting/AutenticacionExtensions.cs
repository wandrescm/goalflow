using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace GoalFlow.Api.Hosting;

public static class AutenticacionExtensions
{
    // Not the default "Bearer": the scheme name is written to the logs (Spec 003 R-06).
    public const string Esquema = "Supabase";
    public const string IssuerKey = "Auth:Issuer";
    public const string JwksUrlKey = "Auth:JwksUrl";
    public const string JwksRefreshSecondsKey = "Auth:JwksRefreshSeconds";
    public const string JwksMinRefreshSecondsKey = "Auth:JwksMinRefreshSeconds";

    private const string Audiencia = "authenticated";
    private const string RolDeSupabase = "authenticated";

    // Options are built on the first Admin request, not at startup: a host without Auth:*
    // still serves /health and the public routes (Spec 002 T-24).
    public static IServiceCollection AddGoalFlowAutenticacion(this IServiceCollection services)
    {
        // WebApplication adds UseAuthentication by itself; with a sole scheme as the automatic default it
        // would authenticate every request, public routes included. Only the Admin filter authenticates.
        AppContext.SetSwitch("Microsoft.AspNetCore.Authentication.SuppressAutoDefaultScheme", true);
        // By default a refresh runs in the background and the rotated key's first token gets 401 (Spec 003 R-04).
        AppContext.SetSwitch("Switch.Microsoft.IdentityModel.UpdateConfigAsBlocking", true);

        services.AddSingleton<IDocumentRetriever>(new HttpDocumentRetriever());
        services.AddAuthentication().AddJwtBearer(Esquema);
        services.AddOptions<JwtBearerOptions>(Esquema)
            .Configure<IConfiguration, IDocumentRetriever>((options, configuration, fuente) =>
            {
                options.MapInboundClaims = false;
                options.ConfigurationManager = CrearGestorDeLlaves(configuration, fuente);
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
                    ValidIssuer = Requerido(configuration, IssuerKey),
                    ValidAudience = Audiencia,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,
                    // The kid must match (R-01); by default every key would be tried.
                    TryAllIssuerSigningKeys = false,
                };
                options.Events = new JwtBearerEvents { OnTokenValidated = ValidarClaimsDeSupabase };
            });

        return services;
    }

    /// <summary>The sub of a token that passed validation, or null when it is not a UUID.</summary>
    public static Guid? UsuarioDe(ClaimsPrincipal? principal) =>
        Guid.TryParseExact(principal?.FindFirstValue(JwtRegisteredClaimNames.Sub), "D", out var usuario) ? usuario : null;

    private static Task ValidarClaimsDeSupabase(TokenValidatedContext context)
    {
        var principal = context.Principal;
        var anonimo = string.Equals(principal?.FindFirstValue("is_anonymous"), "true", StringComparison.OrdinalIgnoreCase);

        if (UsuarioDe(principal) is null || principal?.FindFirstValue("role") != RolDeSupabase || anonimo)
            context.Fail("The token's claims are not accepted.");

        return Task.CompletedTask;
    }

    private static ConfigurationManager<OpenIdConnectConfiguration> CrearGestorDeLlaves(
        IConfiguration configuration, IDocumentRetriever fuente) =>
        new(Requerido(configuration, JwksUrlKey), new LectorDeJwks(), fuente)
        {
            AutomaticRefreshInterval = TimeSpan.FromSeconds(RequeridoEntero(configuration, JwksRefreshSecondsKey)),
            RefreshInterval = TimeSpan.FromSeconds(RequeridoEntero(configuration, JwksMinRefreshSecondsKey)),
        };

    private static string Requerido(IConfiguration configuration, string clave) =>
        configuration[clave] is { Length: > 0 } valor
            ? valor
            : throw new InvalidOperationException($"Missing configuration value {clave}.");

    private static int RequeridoEntero(IConfiguration configuration, string clave) =>
        int.Parse(Requerido(configuration, clave), System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Loads the JWKS directly; no discovery document is involved (Spec 003 C-07).</summary>
    private sealed class LectorDeJwks : IConfigurationRetriever<OpenIdConnectConfiguration>
    {
        public async Task<OpenIdConnectConfiguration> GetConfigurationAsync(
            string address, IDocumentRetriever retriever, CancellationToken cancel)
        {
            var llaves = new JsonWebKeySet(await retriever.GetDocumentAsync(address, cancel));
            var configuracion = new OpenIdConnectConfiguration();
            foreach (var llave in llaves.GetSigningKeys()) configuracion.SigningKeys.Add(llave);
            return configuracion;
        }
    }
}
