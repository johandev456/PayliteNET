using System.Security.Claims;
using AccountService.Security;

namespace AccountService.UnitTests;

public class KeycloakRolesTransformationTests
{
    private static ClaimsPrincipal Principal(string? realmAccess)
    {
        var claims = new List<Claim> { new("sub", "u1") };
        if (realmAccess is not null) claims.Add(new Claim("realm_access", realmAccess, "JSON"));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer", "preferred_username", ClaimTypes.Role));
    }

    [Fact]
    public async Task Copia_los_roles_del_realm()
    {
        var p = await new KeycloakRolesTransformation()
            .TransformAsync(Principal("""{"roles":["CLIENTE","ADMIN"]}"""));

        Assert.True(p.IsInRole("ADMIN"));
        Assert.True(p.IsInRole("CLIENTE"));
        Assert.False(p.IsInRole("SERVICIO"));
    }

    [Fact]
    public async Task Es_idempotente()
    {
        var t = new KeycloakRolesTransformation();
        var p = await t.TransformAsync(await t.TransformAsync(Principal("""{"roles":["ADMIN"]}""")));

        Assert.Single(p.FindAll(ClaimTypes.Role));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("no es json")]
    [InlineData("""{"roles":"ADMIN"}""")]
    public async Task Claim_ausente_o_malformado_no_da_roles(string? realmAccess)
    {
        var p = await new KeycloakRolesTransformation().TransformAsync(Principal(realmAccess));
        Assert.Empty(p.FindAll(ClaimTypes.Role));
    }

    [Fact]
    public async Task Anonimo_no_se_toca()
    {
        var anon = new ClaimsPrincipal(new ClaimsIdentity());
        Assert.Same(anon, await new KeycloakRolesTransformation().TransformAsync(anon));
    }
}
