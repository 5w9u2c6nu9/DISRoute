using System.Collections.Concurrent;
using System.DirectoryServices.Protocols;
using System.Net;
using System.Text;

namespace DISRoute.Server.Operational;

public sealed record LdapGroupResolution(bool Enabled, bool Success, IReadOnlyList<string> Groups, string? Error);

/// <summary>
/// Optional cross-platform LDAP/Active Directory group resolver for Linux-hosted DISRoute Server.
/// Windows-hosted servers still receive native token groups through Negotiate; LDAP supplements
/// claims when the upstream identity does not carry group membership.
/// </summary>
public sealed class LdapGroupResolver(IConfiguration configuration, IWebHostEnvironment environment, ILogger<LdapGroupResolver> logger)
{
    private sealed record CacheEntry(DateTimeOffset ExpiresAtUtc, LdapGroupResolution Resolution);
    private static readonly Action<ILogger, string, Exception?> LookupFailedMessage =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(1101, nameof(LookupFailedMessage)), "LDAP group lookup failed for {UserName}.");
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<LdapGroupResolution> ResolveAsync(string userName, CancellationToken cancellationToken = default)
    {
        var enabled = configuration.GetValue("Security:Ldap:Enabled", false);
        if (!enabled) return new LdapGroupResolution(false, true, [], null);
        if (string.IsNullOrWhiteSpace(userName)) return new LdapGroupResolution(true, false, [], "Authenticated identity has no user name.");

        var cacheMinutes = Math.Clamp(configuration.GetValue("Security:Ldap:CacheMinutes", 5), 1, 60);
        if (_cache.TryGetValue(userName, out var cached) && cached.ExpiresAtUtc > DateTimeOffset.UtcNow) return cached.Resolution;

        try
        {
            var resolution = await Task.Run(() => ResolveCore(userName, cancellationToken), cancellationToken);
            _cache[userName] = new CacheEntry(DateTimeOffset.UtcNow.AddMinutes(cacheMinutes), resolution);
            return resolution;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LookupFailedMessage(logger, userName, ex);
            var failed = new LdapGroupResolution(true, false, [], ex.Message);
            // Cache failures briefly so an LDAP outage cannot create a request storm.
            _cache[userName] = new CacheEntry(DateTimeOffset.UtcNow.AddSeconds(15), failed);
            return failed;
        }
    }

    private LdapGroupResolution ResolveCore(string userName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var host = configuration["Security:Ldap:Host"]?.Trim();
        var baseDn = configuration["Security:Ldap:BaseDn"]?.Trim();
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(baseDn))
            return new LdapGroupResolution(true, false, [], "Security:Ldap:Host and Security:Ldap:BaseDn are required when LDAP is enabled.");

        var mode = configuration["Security:Ldap:Mode"]?.Trim() ?? "Ldaps";
        var useLdaps = mode.Equals("Ldaps", StringComparison.OrdinalIgnoreCase);
        var useStartTls = mode.Equals("StartTls", StringComparison.OrdinalIgnoreCase);
        if (!useLdaps && !useStartTls && environment.IsProduction())
            return new LdapGroupResolution(true, false, [], "Production LDAP requires Ldaps or StartTls.");

        var port = configuration.GetValue<int?>("Security:Ldap:Port") ?? (useLdaps ? 636 : 389);
        var timeoutSeconds = Math.Clamp(configuration.GetValue("Security:Ldap:TimeoutSeconds", 5), 1, 30);
        var identifier = new LdapDirectoryIdentifier(host, port, fullyQualifiedDnsHostName: true, connectionless: false);
        using var connection = new LdapConnection(identifier)
        {
            AuthType = AuthType.Basic,
            Timeout = TimeSpan.FromSeconds(timeoutSeconds)
        };
        connection.SessionOptions.ProtocolVersion = 3;
        connection.SessionOptions.SecureSocketLayer = useLdaps;
        if (useStartTls) connection.SessionOptions.StartTransportLayerSecurity(null);

        var bindDn = configuration["Security:Ldap:BindDn"]?.Trim();
        var secretReference = configuration["Security:Ldap:BindPassword"]?.Trim();
        var secretFile = configuration["Security:Ldap:BindPasswordFile"]?.Trim();
        if (!string.IsNullOrWhiteSpace(bindDn))
        {
            string password;
            if (!string.IsNullOrWhiteSpace(secretReference))
            {
                if (!SecretReferenceResolver.IsSecretReference(secretReference))
                    return new LdapGroupResolution(true, false, [], "Security:Ldap:BindPassword must use a secret:// reference.");
                password = SecretReferenceResolver.ResolveValue(configuration, secretReference, "Security:Ldap:BindPassword");
            }
            else if (!string.IsNullOrWhiteSpace(secretFile))
            {
                password = SecretReferenceResolver.ResolveValue(configuration, "secret://file/" + secretFile, "Security:Ldap:BindPasswordFile");
            }
            else
            {
                return new LdapGroupResolution(true, false, [], "LDAP BindDn requires Security:Ldap:BindPassword secret reference or BindPasswordFile; plaintext bind passwords are not supported.");
            }
            connection.Credential = new NetworkCredential(bindDn, password);
        }

        connection.Bind();
        cancellationToken.ThrowIfCancellationRequested();

        var account = NormalizeAccountName(userName);
        var escapedFull = EscapeFilterValue(userName);
        var escapedAccount = EscapeFilterValue(account);
        var configuredFilter = configuration["Security:Ldap:UserFilter"];
        var filter = string.IsNullOrWhiteSpace(configuredFilter)
            ? $"(|(sAMAccountName={escapedAccount})(userPrincipalName={escapedFull}))"
            : configuredFilter.Replace("{username}", escapedFull, StringComparison.Ordinal)
                              .Replace("{account}", escapedAccount, StringComparison.Ordinal);

        var request = new SearchRequest(baseDn, filter, SearchScope.Subtree, "memberOf", "distinguishedName");
        var response = (SearchResponse)connection.SendRequest(request, TimeSpan.FromSeconds(timeoutSeconds));
        if (response.Entries.Count == 0)
            return new LdapGroupResolution(true, true, [], null);
        if (response.Entries.Count > 1)
            return new LdapGroupResolution(true, false, [], $"LDAP user filter matched {response.Entries.Count} entries; expected exactly one.");

        var groups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entry = response.Entries[0];
        if (entry.Attributes["memberOf"] is DirectoryAttribute memberOf)
        {
            foreach (var value in memberOf.GetValues(typeof(string)).Cast<string>())
            {
                groups.Add(value);
                var cn = ExtractFirstRdn(value, "CN");
                if (!string.IsNullOrWhiteSpace(cn)) groups.Add(cn);
            }
        }
        return new LdapGroupResolution(true, true, groups.Order(StringComparer.OrdinalIgnoreCase).ToArray(), null);
    }

    private static string NormalizeAccountName(string userName)
    {
        var slash = userName.LastIndexOf('\\');
        if (slash >= 0 && slash < userName.Length - 1) return userName[(slash + 1)..];
        var at = userName.IndexOf('@');
        return at > 0 ? userName[..at] : userName;
    }

    private static string EscapeFilterValue(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '\\': builder.Append("\\5c"); break;
                case '*': builder.Append("\\2a"); break;
                case '(': builder.Append("\\28"); break;
                case ')': builder.Append("\\29"); break;
                case '\0': builder.Append("\\00"); break;
                default: builder.Append(ch); break;
            }
        }
        return builder.ToString();
    }

    private static string? ExtractFirstRdn(string distinguishedName, string attribute)
    {
        var prefix = attribute + "=";
        foreach (var component in distinguishedName.Split(','))
        {
            var trimmed = component.Trim();
            if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return trimmed[prefix.Length..].Replace("\\,", ",", StringComparison.Ordinal).Trim();
        }
        return null;
    }
}
