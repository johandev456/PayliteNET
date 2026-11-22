using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

namespace PaymentService.Security;

public static class Politicas
{
    public const string Admin = "Admin";
    public const string Servicio = "Servicio";
}

public static class AuthExtensions
{
    /// <summary>
    /// JWT emitido por Keycloak. Sección de configuración <c>Auth</c>:
    /// <list type="bullet">
    /// <item><c>MetadataAddress</c>: de dónde bajar las llaves (dentro de Docker, http://keycloak:8080/...).</item>
    /// <item><c>Issuer</c>: el <c>iss</c> esperado (siempre http://localhost:8081/realms/paylite gracias a KC_HOSTNAME).</item>
    /// <item><c>Audience</c> / <c>ValidateAudience</c>: el realm añade <c>paylite-api</c> con un audience mapper.</item>
    /// </list>
    /// </summary>
    public static IServiceCollection AddPayliteAuth(this IServiceCollection services, IConfiguration config)
    {
        var auth = config.GetSection("Auth");

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.MetadataAddress = auth["MetadataAddress"] ?? "";
                o.RequireHttpsMetadata = auth.GetValue("RequireHttpsMetadata", true);
                // Sin esto, "sub" se renombra a ClaimTypes.NameIdentifier y "realm_access" se pierde de vista.
                o.MapInboundClaims = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = auth["Issuer"],
                    ValidateAudience = auth.GetValue("ValidateAudience", true),
                    ValidAudience = auth["Audience"],
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "preferred_username",
                    RoleClaimType = ClaimTypes.Role,
                };
            });

        services.AddAuthorization(o =>
        {
            o.AddPolicy(Politicas.Admin, p => p.RequireAuthenticatedUser().RequireRole("ADMIN"));
            o.AddPolicy(Politicas.Servicio, p => p.RequireAuthenticatedUser().RequireRole("SERVICIO"));
            // Todo endpoint exige token salvo que diga [AllowAnonymous].
            o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
        });

        services.AddTransient<IClaimsTransformation, KeycloakRolesTransformation>();
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();
        return services;
    }
}
