using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace PayLite.Testing;

/// <summary>
/// Sustituye a Keycloak en los tests: una llave RSA local firma tokens con la misma forma
/// que los de Keycloak (<c>sub</c>, <c>realm_access.roles</c>, <c>aud</c>). La app valida
/// firma, emisor, audiencia y vencimiento exactamente igual; solo cambia de dónde salen las llaves.
/// </summary>
public static class TestAuth
{
    public const string Issuer = "http://keycloak.test/realms/paylite";
    public const string Audience = "paylite-api";

    private static readonly RsaSecurityKey Llave = new(RSA.Create(2048)) { KeyId = "paylite-test" };
    private static readonly RsaSecurityKey LlaveAjena = new(RSA.Create(2048)) { KeyId = "paylite-test" };

    /// <summary>Configura la app bajo test para confiar en la llave local, sin descargar metadata.</summary>
    public static void Configurar(IWebHostBuilder builder)
    {
        builder.UseSetting("Auth:Issuer", Issuer);
        builder.UseSetting("Auth:Audience", Audience);
        builder.UseSetting("Auth:ValidateAudience", "true");
        builder.UseSetting("Auth:RequireHttpsMetadata", "false");
        builder.UseSetting("Auth:MetadataAddress", "http://keycloak.test/no-usar");

        builder.ConfigureTestServices(s =>
            s.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
            {
                var oidc = new OpenIdConnectConfiguration { Issuer = Issuer };
                oidc.SigningKeys.Add(Llave);
                o.Configuration = oidc;
                o.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(oidc);
            }));
    }

    public static string Token(
        string sub,
        string[]? roles = null,
        string audience = Audience,
        string issuer = Issuer,
        TimeSpan? vence = null,
        bool firmaAjena = false)
    {
        var ahora = DateTime.UtcNow;
        var expira = ahora + (vence ?? TimeSpan.FromMinutes(5));
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = expira < ahora ? expira.AddMinutes(-5) : ahora,
            NotBefore = expira < ahora ? expira.AddMinutes(-5) : ahora,
            Expires = expira,
            Claims = new Dictionary<string, object>
            {
                ["sub"] = sub,
                ["preferred_username"] = sub,
                ["realm_access"] = new Dictionary<string, object> { ["roles"] = roles ?? ["CLIENTE"] },
            },
            SigningCredentials = new SigningCredentials(firmaAjena ? LlaveAjena : Llave, SecurityAlgorithms.RsaSha256),
        });
    }

    public static HttpClient Como(this HttpClient http, string token)
    {
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }
}
