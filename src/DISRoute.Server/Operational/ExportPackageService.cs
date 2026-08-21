using System.IO.Compression;
using System.Text.Json;

namespace DISRoute.Server.Operational;

public sealed class ExportPackageService
{
    private readonly OperationalStore _core;
    private readonly FeatureStore _features;
    private readonly StatePersistenceService _persistence;
    private readonly DurableMutationWriterService _journal;
    private readonly HaCoordinatorService _ha;
    private readonly IConfiguration _configuration;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public ExportPackageService(OperationalStore core, FeatureStore features, StatePersistenceService persistence, DurableMutationWriterService journal, HaCoordinatorService ha, IConfiguration configuration)
    {
        _core = core; _features = features; _persistence = persistence; _journal = journal; _ha = ha; _configuration = configuration;
    }

    public byte[] BuildAfterActionPackage(Guid incidentId)
    {
        var incident = _core.GetIncident(incidentId);
        var payload = new
        {
            generatedAtUtc = DateTimeOffset.UtcNow,
            incident,
            activity = _core.GetActivity(incidentId),
            units = _core.Units.Where(x => x.IncidentId == incidentId).ToArray(),
            replay = _core.GetReplay(incidentId, null, null),
            messages = _core.Messages.Where(x => x.IncidentId == incidentId).ToArray(),
            sharedNarrative = _features.GetCollaborativeNarrative(incidentId),
            calls = _features.Calls.Where(x => x.IncidentId == incidentId).ToArray(),
            dispatchActions = _features.DispatchActions.Where(x => x.IncidentId == incidentId).ToArray(),
            dispatchDeliveries = _features.DispatchDeliveries.Where(x => _features.DispatchActions.Any(a => a.Id == x.DispatchActionId && a.IncidentId == incidentId)).ToArray(),
            stationDeliveries = _features.StationDeliveries.Where(x => x.IncidentId == incidentId).ToArray(),
            runSheets = _features.RunSheets.Where(x => x.IncidentId == incidentId).ToArray(),
            alerts = _features.Alerts.Where(x => x.IncidentId == incidentId).ToArray(),
            mapObjects = _features.MapObjects.Where(x => x.IncidentId == incidentId).ToArray(),
            fireWatches = _features.FireWatches.Where(x => x.IncidentId == incidentId).ToArray(),
            operationalGroups = _features.OperationalGroups.Where(x => x.IncidentId == incidentId).ToArray(),
            commands = _features.Commands.Where(x => x.IncidentId == incidentId).ToArray(),
            mutualAid = _features.MutualAid.Where(x => x.IncidentId == incidentId).ToArray(),
            maydays = _features.Maydays.Where(x => x.IncidentId == incidentId).ToArray(),
            corrections = _features.Corrections.Where(x => x.IncidentId == incidentId).ToArray(),
            rms = _core.RmsRecords.Where(x => x.IncidentId == incidentId).ToArray(),
            rmsIncidentReports = _core.RmsIncidentReports.Where(x => x.IncidentId == incidentId).ToArray(),
            rmsReportVersions = _core.RmsReportVersions.Where(x => _core.RmsIncidentReports.Any(r => r.Id == x.ReportId && r.IncidentId == incidentId)).ToArray(),
            rmsAttachments = _core.RmsAttachments.Where(x => _core.RmsIncidentReports.Any(r => r.Id == x.ReportId && r.IncidentId == incidentId)).ToArray(),
            nerisSubmissions = _core.NerisSubmissions.Where(x => x.IncidentId == incidentId).ToArray(),
            nerisSubmissionAttempts = _core.NerisSubmissions.Where(x => x.IncidentId == incidentId).SelectMany(x => _core.GetNerisSubmissionAttempts(x.Id)).ToArray(),
            neris = _features.NerisReports.Where(x => x.IncidentId == incidentId).ToArray(),
            audit = _features.Audit.Where(x => x.EntityId.Equals(incidentId.ToString(), StringComparison.OrdinalIgnoreCase) || x.Summary.Contains(incident.Number, StringComparison.OrdinalIgnoreCase)).ToArray()
        };
        return Zip(("manifest.json", JsonSerializer.SerializeToUtf8Bytes(new { product = "DISRoute", package = "AfterAction", incidentId, incident.Number, generatedAtUtc = DateTimeOffset.UtcNow }, JsonOptions)),
                   ("incident-package.json", JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions)));
    }

    public byte[] BuildRecordsPackage(DateTimeOffset? fromUtc, DateTimeOffset? toUtc)
    {
        var from = fromUtc ?? DateTimeOffset.MinValue;
        var to = toUtc ?? DateTimeOffset.MaxValue;
        var incidents = _core.GetIncidents().Where(x => x.ReceivedAtUtc >= from && x.ReceivedAtUtc <= to).ToArray();
        var incidentIds = incidents.Select(x => x.Id).ToHashSet();
        var payload = new
        {
            generatedAtUtc = DateTimeOffset.UtcNow,
            range = new { fromUtc, toUtc },
            incidents,
            activity = incidents.SelectMany(x => _core.GetActivity(x.Id)).ToArray(),
            replay = _core.GetReplay(null, fromUtc, toUtc),
            messages = _core.Messages.Where(x => x.SentAtUtc >= from && x.SentAtUtc <= to).ToArray(),
            rms = _core.RmsRecords.Where(x => x.CreatedAtUtc >= from && x.CreatedAtUtc <= to).ToArray(),
            rmsIncidentReports = _core.RmsIncidentReports.Where(x => x.CreatedAtUtc >= from && x.CreatedAtUtc <= to).ToArray(),
            rmsReportVersions = _core.RmsReportVersions.Where(x => x.CreatedAtUtc >= from && x.CreatedAtUtc <= to).ToArray(),
            rmsAttachments = _core.RmsAttachments.Where(x => x.AddedAtUtc >= from && x.AddedAtUtc <= to).ToArray(),
            nerisSubmissions = _core.NerisSubmissions.Where(x => x.CreatedAtUtc >= from && x.CreatedAtUtc <= to).ToArray(),
            nerisSubmissionAttempts = _core.NerisSubmissions.Where(x => x.CreatedAtUtc >= from && x.CreatedAtUtc <= to).SelectMany(x => _core.GetNerisSubmissionAttempts(x.Id)).ToArray(),
            neris = _features.NerisReports.Where(x => incidentIds.Contains(x.IncidentId)).ToArray(),
            audit = _features.Audit.Where(x => x.OccurredAtUtc >= from && x.OccurredAtUtc <= to).ToArray()
        };
        return Zip(("manifest.json", JsonSerializer.SerializeToUtf8Bytes(new { product = "DISRoute", package = "Records", generatedAtUtc = DateTimeOffset.UtcNow, fromUtc, toUtc }, JsonOptions)),
                   ("records.json", JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions)));
    }

    public byte[] BuildSupportBundle()
    {
        var safeConfig = new
        {
            release = ProductionDeploymentSafety.ReleaseVersion,
            configurationFingerprint = ProductionDeploymentSafety.ConfigurationFingerprint(_configuration),
            persistenceProvider = _configuration["Persistence:Provider"],
            requireAuthentication = _configuration.GetValue("Security:RequireAuthentication", true),
            requireHttps = _configuration.GetValue("Security:RequireHttps", true),
            requireMfa = _configuration.GetValue("Security:RequireMfa", true),
            reverseProxy = _configuration.GetValue("Security:ReverseProxy:Enabled", false),
            rateLimitPerMinute = _configuration.GetValue("Security:RateLimit:PermitLimitPerMinute", 600),
            haEnabled = _configuration.GetValue("HighAvailability:Enabled", false),
            nodeId = _configuration["HighAvailability:NodeId"],
            // Deliberately exclude connection strings, tokens, secret references and key material.
        };
        var bundle = new
        {
            generatedAtUtc = DateTimeOffset.UtcNow,
            process = new { environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), machine = Environment.MachineName, os = Environment.OSVersion.ToString(), framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription, uptimeSeconds = Environment.TickCount64 / 1000 },
            config = safeConfig,
            persistence = _persistence.Status,
            mutationJournal = _journal.Status,
            ha = _ha.Status,
            providers = _features.ProviderHealth,
            clients = _features.Heartbeats,
            devices = _features.Devices.Select(x => new { x.DeviceId, x.Name, x.Role, x.Platform, x.State, x.LastSeenAtUtc }).ToArray(),
            activeAlerts = _features.Alerts.Where(x => x.State != "Resolved").ToArray(),
            backups = _features.HaStatus().Backups,
            replication = _features.HaStatus().Replicas,
            auditIntegrity = _features.VerifyAuditIntegrity(),
            recentAudit = _features.Audit.Take(500).ToArray()
        };
        return Zip(("support.json", JsonSerializer.SerializeToUtf8Bytes(bundle, JsonOptions)));
    }

    private static byte[] Zip(params (string Name, byte[] Content)[] entries)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            foreach (var entry in entries)
            {
                var zipEntry = archive.CreateEntry(entry.Name, CompressionLevel.SmallestSize);
                using var stream = zipEntry.Open();
                stream.Write(entry.Content);
            }
        }
        return output.ToArray();
    }
}
