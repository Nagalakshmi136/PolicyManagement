using System.ComponentModel.DataAnnotations;

namespace PolicyManagement.Api.Options;

/// <summary>
/// Strongly-typed options for Keycloak JWT Bearer authentication.
/// Bound from the <c>Keycloak</c> configuration section.
/// </summary>
public sealed class KeycloakOptions
{
    public const string SectionName = "Keycloak";

    /// <summary>
    /// Keycloak realm authority URL, e.g. <c>http://keycloak:8080/realms/policy-mgmt</c>.
    /// Environment variable: <c>Keycloak__Authority</c>.
    /// </summary>
    [Required, Url]
    public string Authority { get; init; } = string.Empty;

    /// <summary>
    /// Expected audience claim value, e.g. <c>policy-management-api</c>.
    /// Environment variable: <c>Keycloak__Audience</c>.
    /// </summary>
    [Required]
    public string Audience { get; init; } = string.Empty;
}
