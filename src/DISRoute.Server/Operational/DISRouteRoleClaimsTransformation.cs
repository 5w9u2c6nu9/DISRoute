using System.Security.Claims;
using System.Security.Principal;
using DISRoute.Security;
using Microsoft.AspNetCore.Authentication;

namespace DISRoute.Server.Operational;

public sealed class DISRouteRoleClaimsTransformation(IConfiguration configuration, LdapGroupResolver ldap) : IClaimsTransformation
{
    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity identity || !identity.IsAuthenticated) return principal;
        var existingRoles = identity.Claims.Where(c => c.Type is ClaimTypes.Role or "role" or "roles").Select(c => c.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var groups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var claim in identity.Claims)
        {
            if (claim.Type.Contains("group", StringComparison.OrdinalIgnoreCase)) groups.Add(claim.Value);
        }

        if (OperatingSystem.IsWindows() && identity is WindowsIdentity windowsIdentity && windowsIdentity.Groups is { } windowsGroups)
        {
            foreach (IdentityReference sid in windowsGroups)
            {
                try
                {
                    groups.Add(sid.Value);
                    if (sid.Translate(typeof(NTAccount)) is NTAccount account)
                    {
                        groups.Add(account.Value);
                        var slash = account.Value.LastIndexOf('\\');
                        if (slash >= 0 && slash < account.Value.Length - 1) groups.Add(account.Value[(slash + 1)..]);
                    }
                }
                catch (IdentityNotMappedException) { }
            }
        }

        var userName = identity.Name ?? identity.FindFirst(ClaimTypes.Name)?.Value ?? string.Empty;
        var ldapResolution = await ldap.ResolveAsync(userName);
        if (ldapResolution.Enabled && ldapResolution.Success)
            groups.UnionWith(ldapResolution.Groups);
        else if (ldapResolution.Enabled && !ldapResolution.Success && configuration.GetValue("Security:Ldap:Required", false))
            identity.AddClaim(new Claim("disroute:identity-degraded", ldapResolution.Error ?? "LDAP group lookup failed."));

        var mapped = AdGroupRoleMapper.Map(groups);
        var configured = configuration.GetSection("Security:GroupRoleMap").Get<Dictionary<string, string>>() ?? [];
        foreach (var group in groups)
        {
            if (configured.TryGetValue(group, out var role) && !string.IsNullOrWhiteSpace(role))
                mapped = mapped.Append(role.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        foreach (var role in mapped)
        {
            if (existingRoles.Add(role)) identity.AddClaim(new Claim(ClaimTypes.Role, role));
        }
        return principal;
    }
}
