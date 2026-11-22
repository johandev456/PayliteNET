using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;

namespace AccountService.Security;

/// <summary>
/// Keycloak pone los roles del realm en <c>realm_access.roles</c> (un objeto JSON),
/// no en un claim plano. Esta transformación los copia como claims de rol para que
/// <c>[Authorize(Roles = ...)]</c> y <c>RequireRole</c> funcionen.
/// ASP.NET Core puede invocarla más de una vez por petición: por eso es idempotente
/// (marca la identidad y no vuelve a añadir roles).
/// </summary>
public sealed class KeycloakRolesTransformation : IClaimsTransformation
{
    private const string Marcador = "paylite:roles_mapeados";

    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity { IsAuthenticated: true } identidad
            || identidad.HasClaim(c => c.Type == Marcador))
            return Task.FromResult(principal);

        var copia = principal.Clone();
        var id = (ClaimsIdentity)copia.Identity!;
        foreach (var rol in LeerRoles(id.FindFirst("realm_access")?.Value))
        {
            if (!id.HasClaim(id.RoleClaimType, rol))
                id.AddClaim(new Claim(id.RoleClaimType, rol));
        }
        id.AddClaim(new Claim(Marcador, "1"));
        return Task.FromResult(copia);
    }

    internal static IEnumerable<string> LeerRoles(string? realmAccess)
    {
        if (string.IsNullOrWhiteSpace(realmAccess))
            return [];
        try
        {
            using var doc = JsonDocument.Parse(realmAccess);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("roles", out var roles)
                && roles.ValueKind == JsonValueKind.Array)
            {
                return roles.EnumerateArray()
                    .Where(r => r.ValueKind == JsonValueKind.String)
                    .Select(r => r.GetString()!)
                    .ToList();
            }
        }
        catch (JsonException)
        {
            // Claim mal formado: sin roles. Nunca 500 por un token raro.
        }
        return [];
    }
}
