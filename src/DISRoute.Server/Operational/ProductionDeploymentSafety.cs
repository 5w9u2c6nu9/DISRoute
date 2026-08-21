using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Security.Cryptography.X509Certificates;

namespace DISRoute.Server.Operational;

public sealed record DeploymentCheckDto(string Key, string State, string Detail, bool Blocking);
public sealed record DeploymentReadinessDto(
    string Release,
    string Environment,
    string ConfigurationFingerprint,
    string TlsMode,
    long FreeDiskBytes,
    IReadOnlyList<DeploymentCheckDto> Checks,
    DateTimeOffset AsOfUtc)
{
    public bool Ready => Checks.All(x => !x.Blocking || x.State.Equals("Pass", StringComparison.OrdinalIgnoreCase));
}

public sealed record UpgradePreflightDto(
    string Release,
    int SupportedSchemaVersion,
    bool SafeToUpgrade,
    IReadOnlyList<DeploymentCheckDto> Checks,
    DateTimeOffset AsOfUtc);

public static class ProductionDeploymentSafety
{
    public static string ReleaseVersion =>
        typeof(ProductionDeploymentSafety).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(ProductionDeploymentSafety).Assembly.GetName().Version?.ToString()
        ?? "unknown";

    public static void ValidateStartup(IConfiguration configuration, IHostEnvironment environment)
    {
        if (!environment.IsProduction()) return;

        if (!string.Equals(configuration["Persistence:Provider"], "PostgreSQL", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Production DISRoute requires Persistence:Provider=PostgreSQL.");
        if (!configuration.GetValue("Security:RequireAuthentication", true))
            throw new InvalidOperationException("Production DISRoute cannot disable authentication.");
        if (!configuration.GetValue("Security:RequireHttps", true))
            throw new InvalidOperationException("Production DISRoute cannot disable HTTPS enforcement.");
        if (!configuration.GetValue("Security:RequireMfa", true))
            throw new InvalidOperationException("Production DISRoute cannot disable MFA policy.");

        _ = SecretReferenceResolver.ResolveConnectionString(configuration);
        SecretReferenceResolver.RequireReferenceWhenPresent(configuration, "HighAvailability:WitnessConnectionString", production: true);
        SecretReferenceResolver.RequireReferenceWhenPresent(configuration, "Integrations:StationAlert:BearerToken", production: true);
        SecretReferenceResolver.RequireReferenceWhenPresent(configuration, "Integrations:FireReporting:BearerToken", production: true);
        SecretReferenceResolver.RequireReferenceWhenPresent(configuration, "Security:Ldap:BindPassword", production: true);
        SecretReferenceResolver.RequireReferenceWhenPresent(configuration, "Security:Tls:CertificatePassword", production: true);

        var maxBody = configuration.GetValue<long?>("Security:MaxRequestBodyBytes") ?? 10 * 1024 * 1024;
        if (maxBody < 1024 || maxBody > 100L * 1024 * 1024)
            throw new InvalidOperationException("Security:MaxRequestBodyBytes must be between 1 KiB and 100 MiB in Production.");

        var rateLimit = configuration.GetValue("Security:RateLimit:PermitLimitPerMinute", 600);
        if (rateLimit < 60 || rateLimit > 100_000)
            throw new InvalidOperationException("Security:RateLimit:PermitLimitPerMinute must be between 60 and 100000 in Production.");

        var allowInsecureProviderHttp = configuration.GetValue("Security:AllowInsecureProviderHttp", false);
        if (!allowInsecureProviderHttp)
        {
            foreach (var key in new[] { "Integrations:StationAlert:Endpoint", "Integrations:FireReporting:Endpoint" })
            {
                var value = configuration[key];
                if (Uri.TryCreate(value, UriKind.Absolute, out var endpoint) && endpoint.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Production provider endpoint '{key}' must use HTTPS unless Security:AllowInsecureProviderHttp=true is explicitly accepted.");
            }
        }

        var reverseProxy = configuration.GetValue("Security:ReverseProxy:Enabled", false);
        if (reverseProxy)
        {
            var proxies = (configuration.GetSection("Security:ReverseProxy:KnownProxies").Get<string[]>() ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
            var networks = (configuration.GetSection("Security:ReverseProxy:KnownNetworks").Get<string[]>() ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
            if (proxies.Length == 0 && networks.Length == 0)
                throw new InvalidOperationException("Security:ReverseProxy:Enabled requires at least one KnownProxies or KnownNetworks entry; trusting arbitrary forwarded headers is forbidden.");
        }
        else
        {
            var certificatePath = configuration["Security:Tls:CertificatePath"]?.Trim();
            if (string.IsNullOrWhiteSpace(certificatePath) || !File.Exists(Path.GetFullPath(certificatePath)))
                throw new InvalidOperationException("Direct Production TLS requires an existing Security:Tls:CertificatePath. Use a trusted reverse proxy explicitly if TLS terminates elsewhere.");
            var password = SecretReferenceResolver.ResolveOptional(configuration, "Security:Tls:CertificatePassword");
            using var certificate = X509CertificateLoader.LoadPkcs12FromFile(Path.GetFullPath(certificatePath), password);
            var minimumDays = Math.Clamp(configuration.GetValue("Security:Tls:MinimumRemainingDays", 14), 1, 365);
            if (certificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow.AddDays(minimumDays))
                throw new InvalidOperationException($"Production TLS certificate expires too soon ({certificate.NotAfter:O}); at least {minimumDays} days remaining are required at startup.");
            if (!OperatingSystem.IsWindows())
            {
                var mode = File.GetUnixFileMode(Path.GetFullPath(certificatePath));
                var forbidden = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
                if ((mode & forbidden) != 0) throw new InvalidOperationException("Production TLS certificate/private-key file must not grant group/other permissions.");
            }
        }

        var topology = configuration["HighAvailability:DatabaseTopology"]?.Trim();
        if (string.Equals(topology, HaDatabaseTopologySafety.PhysicalFailover, StringComparison.OrdinalIgnoreCase))
        {
            var witness = SecretReferenceResolver.ResolveOptional(configuration, "HighAvailability:WitnessConnectionString");
            var fencing = configuration["HighAvailability:DatabaseFencingMode"]?.Trim();
            if (string.IsNullOrWhiteSpace(witness) || !string.Equals(fencing, HaDatabaseTopologySafety.ExternalFencing, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("PhysicalFailover production deployment requires an independent witness and DatabaseFencingMode=External.");
        }
    }

    public static string ConfigurationFingerprint(IConfiguration configuration)
    {
        var safe = new[]
        {
            $"persistence={configuration["Persistence:Provider"]}",
            $"ha={configuration.GetValue("HighAvailability:Enabled", false)}",
            $"site={configuration["HighAvailability:Site"]}",
            $"role={configuration["HighAvailability:ConfiguredRole"]}",
            $"topology={configuration["HighAvailability:DatabaseTopology"]}",
            $"fencing={configuration["HighAvailability:DatabaseFencingMode"]}",
            $"auth={configuration.GetValue("Security:RequireAuthentication", true)}",
            $"https={configuration.GetValue("Security:RequireHttps", true)}",
            $"mfa={configuration.GetValue("Security:RequireMfa", true)}",
            $"reverseProxy={configuration.GetValue("Security:ReverseProxy:Enabled", false)}",
            $"requestBytes={configuration.GetValue<long?>("Security:MaxRequestBodyBytes") ?? 0}",
            $"rate={configuration.GetValue("Security:RateLimit:PermitLimitPerMinute", 0)}"
        };
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', safe)))).ToLowerInvariant();
    }
}

public sealed class OperationalReadinessService
{
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly StatePersistenceService _persistence;
    private readonly DurableMutationWriterService _journal;
    private readonly HaCoordinatorService _ha;
    private readonly FeatureStore _features;

    public OperationalReadinessService(
        IConfiguration configuration,
        IHostEnvironment environment,
        StatePersistenceService persistence,
        DurableMutationWriterService journal,
        HaCoordinatorService ha,
        FeatureStore features)
    {
        _configuration = configuration;
        _environment = environment;
        _persistence = persistence;
        _journal = journal;
        _ha = ha;
        _features = features;
    }

    public DeploymentReadinessDto GetDeploymentReadiness()
    {
        var checks = new List<DeploymentCheckDto>();
        var persistence = _persistence.Status;
        var journal = _journal.Status;
        var ha = _ha.Status;
        var journalLag = Math.Max(0, journal.Queued - journal.Persisted);

        Add(checks, "persistence", persistence.State.Equals("Healthy", StringComparison.OrdinalIgnoreCase), $"{persistence.Provider} {persistence.DatabaseRole}; migration-integrity={persistence.MigrationIntegrityVerified}", true);
        Add(checks, "mutation-journal", journalLag == 0 && journal.PendingAuthoritativeBatches == 0 && string.IsNullOrWhiteSpace(journal.LastError), $"lag={journalLag}; pending-batches={journal.PendingAuthoritativeBatches}; last-error={journal.LastError ?? "none"}", true);
        Add(checks, "ha-fencing", !ha.AcceptingWrites || ha.LeaseEpoch >= 0, $"state={ha.State}; role={ha.Role}; accepting-writes={ha.AcceptingWrites}; epoch={ha.LeaseEpoch}", true);

        var ambiguous = _features.IntegrationOperations.Count(x => x.State is "Submitting" or "NeedsReconciliation");
        Add(checks, "provider-reconciliation", ambiguous == 0, $"ambiguous-operations={ambiguous}", false);

        var minFree = _configuration.GetValue<long?>("Operations:MinimumFreeDiskBytes") ?? 1024L * 1024 * 1024;
        var free = GetFreeDiskBytes();
        Add(checks, "disk-space", free < 0 || free >= minFree, free < 0 ? "free-space unavailable" : $"free-bytes={free}; minimum={minFree}", true);

        var backupMaxAgeHours = Math.Clamp(_configuration.GetValue("Operations:BackupWarningHours", 24), 1, 24 * 30);
        var backup = _features.HaStatus().Backups.OrderByDescending(x => x.LastSuccessfulBackupUtc).FirstOrDefault();
        var backupCurrent = backup?.LastSuccessfulBackupUtc is DateTimeOffset at && at >= DateTimeOffset.UtcNow.AddHours(-backupMaxAgeHours);
        Add(checks, "backup-recency", backupCurrent, backup?.LastSuccessfulBackupUtc is DateTimeOffset last ? $"last-success={last:O}; warning-hours={backupMaxAgeHours}" : "no successful portable backup recorded", false);

        return new DeploymentReadinessDto(
            ProductionDeploymentSafety.ReleaseVersion,
            _environment.EnvironmentName,
            ProductionDeploymentSafety.ConfigurationFingerprint(_configuration),
            _configuration.GetValue("Security:ReverseProxy:Enabled", false) ? "TrustedReverseProxy" : "DirectKestrelTls",
            free,
            checks,
            DateTimeOffset.UtcNow);
    }

    public UpgradePreflightDto GetUpgradePreflight()
    {
        var checks = new List<DeploymentCheckDto>();
        var persistence = _persistence.Status;
        var journal = _journal.Status;
        var ha = _ha.Status;
        var journalLag = Math.Max(0, journal.Queued - journal.Persisted);

        Add(checks, "schema-current", persistence.SupportedSchemaVersion > 0 && persistence.MigrationIntegrityVerified != false, $"supported-schema={persistence.SupportedSchemaVersion}; ledger-verified={persistence.MigrationIntegrityVerified}", true);
        Add(checks, "authoritative-primary", ha.AcceptingWrites, $"node={ha.NodeId}; role={ha.Role}; state={ha.State}; epoch={ha.LeaseEpoch}", true);
        Add(checks, "journal-drained", journalLag == 0 && journal.PendingAuthoritativeBatches == 0, $"lag={journalLag}; pending-batches={journal.PendingAuthoritativeBatches}", true);
        var submitting = _features.IntegrationOperations.Count(x => x.State == "Submitting");
        Add(checks, "provider-submit-boundary", submitting == 0, $"submitting-operations={submitting}", true);
        var reconciliation = _features.IntegrationOperations.Count(x => x.State == "NeedsReconciliation");
        Add(checks, "provider-reconciliation", reconciliation == 0, $"needs-reconciliation={reconciliation}", false);

        return new UpgradePreflightDto(
            ProductionDeploymentSafety.ReleaseVersion,
            persistence.SupportedSchemaVersion,
            checks.All(x => !x.Blocking || x.State == "Pass"),
            checks,
            DateTimeOffset.UtcNow);
    }

    private long GetFreeDiskBytes()
    {
        try
        {
            var path = _persistence.Provider.Equals("FileCheckpoint", StringComparison.OrdinalIgnoreCase) ? _persistence.Location : AppContext.BaseDirectory;
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrWhiteSpace(root)) return -1;
            return new DriveInfo(root).AvailableFreeSpace;
        }
        catch { return -1; }
    }

    private static void Add(List<DeploymentCheckDto> checks, string key, bool pass, string detail, bool blocking) =>
        checks.Add(new DeploymentCheckDto(key, pass ? "Pass" : blocking ? "Fail" : "Warning", detail, blocking));
}
