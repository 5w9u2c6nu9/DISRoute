using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DISRoute.Contracts.Operational;
using DISRoute.LegacyBridge;

if (args.Length == 0 || args.Contains("--help"))
{
    Console.WriteLine("DISRoute.LegacyBridge\n\n" +
        "validate <staging.json>\n" +
        "import <staging.json> --server <url> [--dry-run] [--report <path>]\n\n" +
        "The bridge consumes a neutral read-only staging export. Vendor/ASNA extraction stays isolated from the modern runtime.");
    return;
}

var command = args[0].ToLowerInvariant();
if (args.Length < 2) throw new ArgumentException("A staging JSON path is required.");
var path = Path.GetFullPath(args[1]);
var staging = await LegacyStagingFile.LoadAsync(path);
var validation = staging.Validate();

if (command == "validate")
{
    PrintValidation(validation);
    Environment.ExitCode = validation.Errors.Count == 0 ? 0 : 2;
    return;
}
if (command != "import") throw new ArgumentException($"Unknown command '{command}'.");

var server = GetOption(args, "--server") ?? "http://127.0.0.1:5099/";
var reportPath = GetOption(args, "--report") ?? Path.Combine(Environment.CurrentDirectory, $"disroute-migration-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
var dryRun = args.Contains("--dry-run", StringComparer.OrdinalIgnoreCase);
if (validation.Errors.Count > 0)
{
    PrintValidation(validation);
    throw new InvalidDataException("Migration staging failed validation; no import was attempted.");
}

using var http = new HttpClient { BaseAddress = new Uri(server), Timeout = TimeSpan.FromSeconds(30) };
var report = await MigrationRunner.RunAsync(staging, http, dryRun);
await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, JsonOptions.Indented));
Console.WriteLine($"Migration {(dryRun ? "dry run" : "import")} complete. report={reportPath}");
Console.WriteLine($"incidents={report.IncidentsImported} warnings={report.Warnings.Count} errors={report.Errors.Count}");
Environment.ExitCode = report.Errors.Count == 0 ? 0 : 3;

static string? GetOption(string[] source, string name)
{
    for (var i = 0; i < source.Length - 1; i++) if (source[i].Equals(name, StringComparison.OrdinalIgnoreCase)) return source[i + 1];
    return null;
}
static void PrintValidation(MigrationValidation validation)
{
    foreach (var warning in validation.Warnings) Console.WriteLine("WARNING: " + warning);
    foreach (var error in validation.Errors) Console.Error.WriteLine("ERROR: " + error);
    Console.WriteLine($"records={validation.RecordCount} warnings={validation.Warnings.Count} errors={validation.Errors.Count}");
}

namespace DISRoute.LegacyBridge
{
public sealed record LegacyStagingFile(int ContractVersion, string SourceSystem, string SourceProfile, string SourceDatabase, string BatchId, DateTimeOffset ExtractedAtUtc, IReadOnlyList<LegacyIncidentRecord> Incidents)
{
    public static async Task<LegacyStagingFile> LoadAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<LegacyStagingFile>(stream, JsonOptions.Web) ?? throw new InvalidDataException("Staging file is empty or invalid JSON.");
    }

    public MigrationValidation Validate()
    {
        var warnings = new List<string>();
        var errors = new List<string>();
        if (ContractVersion != 1) errors.Add($"Unsupported staging contract version {ContractVersion}.");
        if (string.IsNullOrWhiteSpace(SourceSystem)) errors.Add("SourceSystem is required.");
        if (string.IsNullOrWhiteSpace(BatchId)) errors.Add("BatchId is required.");
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in Incidents)
        {
            if (string.IsNullOrWhiteSpace(item.SourceKey)) errors.Add("Incident SourceKey is required.");
            else if (!keys.Add(item.SourceKey)) errors.Add($"Duplicate SourceKey '{item.SourceKey}'.");
            if (string.IsNullOrWhiteSpace(item.Nature)) warnings.Add($"{item.SourceKey}: missing nature; 'Legacy Import' will be used.");
            if (string.IsNullOrWhiteSpace(item.Address)) warnings.Add($"{item.SourceKey}: missing address; manual review required.");
        }
        return new MigrationValidation(Incidents.Count, warnings, errors);
    }
}

public sealed record LegacyIncidentRecord(string SourceKey, string? SourceTable, string? SourceMember, string? OriginalNumber, string Nature, string Priority, string Address, string CrossStreets, string Municipality, string CallerName, string CallerPhone, string Notes, DateTimeOffset? ReceivedAtUtc, Dictionary<string, string?> OriginalValues);
public sealed record MigrationValidation(int RecordCount, IReadOnlyList<string> Warnings, IReadOnlyList<string> Errors);
public sealed record MigrationItemResult(string SourceKey, string State, Guid? DestinationId, string? DestinationNumber, string? Detail, string Checksum);
public sealed record MigrationReport(string BatchId, string SourceSystem, DateTimeOffset StartedAtUtc, DateTimeOffset CompletedAtUtc, bool DryRun, int IncidentsImported, IReadOnlyList<MigrationItemResult> Items, IReadOnlyList<string> Warnings, IReadOnlyList<string> Errors);

public static class MigrationRunner
{
    private static readonly Guid DefaultAgencyId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    public static async Task<MigrationReport> RunAsync(LegacyStagingFile staging, HttpClient http, bool dryRun)
    {
        var started = DateTimeOffset.UtcNow;
        var items = new List<MigrationItemResult>();
        var warnings = staging.Validate().Warnings.ToList();
        var errors = new List<string>();
        StationDto[] stations = [];
        if (!dryRun)
        {
            stations = await http.GetFromJsonAsync<StationDto[]>("api/stations") ?? [];
            if (stations.Length == 0) throw new InvalidOperationException("Destination has no stations configured.");
        }

        foreach (var legacy in staging.Incidents)
        {
            var checksum = Sha256(JsonSerializer.Serialize(legacy, JsonOptions.Web));
            if (string.IsNullOrWhiteSpace(legacy.Address))
            {
                items.Add(new MigrationItemResult(legacy.SourceKey, "ManualReview", null, null, "Address is missing.", checksum));
                continue;
            }
            if (dryRun)
            {
                items.Add(new MigrationItemResult(legacy.SourceKey, "WouldImport", null, legacy.OriginalNumber, null, checksum));
                continue;
            }
            try
            {
                var idempotency = $"legacy:{staging.BatchId}:{legacy.SourceKey}";
                var request = new CreateIncidentRequest(DefaultAgencyId, stations[0].Id,
                    string.IsNullOrWhiteSpace(legacy.Nature) ? "Legacy Import" : legacy.Nature,
                    string.IsNullOrWhiteSpace(legacy.Priority) ? "Routine" : legacy.Priority,
                    legacy.Address, legacy.CrossStreets ?? string.Empty, legacy.Municipality ?? string.Empty,
                    legacy.CallerName ?? string.Empty, legacy.CallerPhone ?? string.Empty,
                    $"[Legacy {staging.SourceSystem} {legacy.SourceKey}] {legacy.Notes}", idempotency);
                using var response = await http.PostAsJsonAsync("api/incidents", request);
                if (!response.IsSuccessStatusCode)
                {
                    var detail = await response.Content.ReadAsStringAsync();
                    errors.Add($"{legacy.SourceKey}: HTTP {(int)response.StatusCode} {detail}");
                    items.Add(new MigrationItemResult(legacy.SourceKey, "Error", null, null, detail, checksum));
                    continue;
                }
                var created = await response.Content.ReadFromJsonAsync<IncidentDto>();
                items.Add(new MigrationItemResult(legacy.SourceKey, "Imported", created?.Id, created?.Number, null, checksum));
            }
            catch (Exception ex)
            {
                errors.Add($"{legacy.SourceKey}: {ex.Message}");
                items.Add(new MigrationItemResult(legacy.SourceKey, "Error", null, null, ex.Message, checksum));
            }
        }

        return new MigrationReport(staging.BatchId, staging.SourceSystem, started, DateTimeOffset.UtcNow, dryRun,
            items.Count(x => x.State == "Imported"), items, warnings, errors);
    }
    private static string Sha256(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}

public static class JsonOptions
{
    public static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
    public static readonly JsonSerializerOptions Indented = new(JsonSerializerDefaults.Web) { WriteIndented = true };
}

}
