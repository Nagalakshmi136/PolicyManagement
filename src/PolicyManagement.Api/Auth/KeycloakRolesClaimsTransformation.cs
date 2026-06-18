using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;

namespace PolicyManagement.Api.Auth;

/// <summary>
/// Transforms Keycloak JWT claims into ASP.NET Core role claims.
/// Keycloak emits realm roles under <c>realm_access.roles</c>, not as a flat
/// <c>roles</c> claim. This transformer maps them to <see cref="ClaimTypes.Role"/>
/// so that named authorization policies (<c>PolicyRead</c>, <c>PolicyWrite</c>)
/// work correctly.
/// </summary>
/// <remarks>
/// Registered as <c>Transient</c> — ASP.NET Core calls this per-request.
/// The idempotency guard prevents double-adding roles when called more than once.
/// </remarks>
public sealed class KeycloakRolesClaimsTransformation : IClaimsTransformation
{
    private const string RealmAccessClaimType = "realm_access";
    private const string RolesKey             = "roles";

    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        // Idempotency guard — avoid adding duplicate role claims
        if (principal.HasClaim(c => c.Type == ClaimTypes.Role))
            return Task.FromResult(principal);

        var realmAccessClaim = principal.FindFirst(RealmAccessClaimType);
        if (realmAccessClaim is null)
            return Task.FromResult(principal);

        using var realmAccess = JsonDocument.Parse(realmAccessClaim.Value);

        if (!realmAccess.RootElement.TryGetProperty(RolesKey, out var rolesElement))
            return Task.FromResult(principal);

        var identity = new ClaimsIdentity();
        foreach (var role in rolesElement.EnumerateArray())
        {
            var roleName = role.GetString();
            if (!string.IsNullOrWhiteSpace(roleName))
                identity.AddClaim(new Claim(ClaimTypes.Role, roleName));
        }

        principal.AddIdentity(identity);
        return Task.FromResult(principal);
    }
}
