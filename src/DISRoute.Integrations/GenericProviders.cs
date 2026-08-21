using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace DISRoute.Integrations;

public sealed record HttpProviderOptions(string Key, string DisplayName, string Category, Uri Endpoint, string? BearerToken, int TimeoutSeconds = 10);

public abstract class HttpProviderBase(HttpProviderOptions options) : IProvider, IDisposable
{
    protected HttpProviderOptions Options { get; } = options;
    protected HttpClient Client { get; } = CreateClient(options);
    private DateTimeOffset? _lastSuccess;

    public string Key => Options.Key;
    public string DisplayName => Options.DisplayName;
    public string Category => Options.Category;

    public virtual ValueTask<ProviderValidationResult> ValidateConfigurationAsync(CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        if (Options.Endpoint.Scheme is not ("http" or "https")) errors.Add("Endpoint must be HTTP or HTTPS.");
        if (Options.TimeoutSeconds is < 1 or > 120) errors.Add("Timeout must be between 1 and 120 seconds.");
        return ValueTask.FromResult(new ProviderValidationResult(errors.Count == 0, errors, []));
    }

    public virtual ValueTask<IReadOnlyList<ProviderCapability>> GetCapabilitiesAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<ProviderCapability>>([]);

    public virtual async ValueTask<ProviderHealth> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, Options.Endpoint);
            using var response = await Client.SendAsync(request, cancellationToken);
            if ((int)response.StatusCode < 500) _lastSuccess = DateTimeOffset.UtcNow;
            return new ProviderHealth(Key, response.IsSuccessStatusCode ? ProviderAvailability.Available : ProviderAvailability.Degraded, $"HTTP {(int)response.StatusCode}", DateTimeOffset.UtcNow, _lastSuccess);
        }
        catch (Exception ex)
        {
            return new ProviderHealth(Key, ProviderAvailability.Unavailable, ex.Message, DateTimeOffset.UtcNow, _lastSuccess);
        }
    }

    protected async ValueTask<ProviderOperationResult> PostJsonAsync<T>(ProviderOperationContext context, T payload, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await Client.PostAsJsonAsync(Options.Endpoint, payload, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                return new ProviderOperationResult(context.OperationId, "Failed", null, $"HTTP {(int)response.StatusCode}: {Trim(body, 512)}", DateTimeOffset.UtcNow);
            _lastSuccess = DateTimeOffset.UtcNow;
            var reference = response.Headers.TryGetValues("X-Provider-Reference", out var values) ? values.FirstOrDefault() : null;
            return new ProviderOperationResult(context.OperationId, "Confirmed", reference ?? Trim(body, 128), null, DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            return new ProviderOperationResult(context.OperationId, "Failed", null, ex.Message, DateTimeOffset.UtcNow);
        }
    }

    private static HttpClient CreateClient(HttpProviderOptions options)
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 1, 120)) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DISRoute/1.0");
        if (!string.IsNullOrWhiteSpace(options.BearerToken)) client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", options.BearerToken);
        return client;
    }

    private static string Trim(string? value, int max) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Length <= max ? value : value[..max];
    public void Dispose()
    {
        Client.Dispose();
        GC.SuppressFinalize(this);
    }
}

public sealed class HttpJsonStationAlertProvider(HttpProviderOptions options) : HttpProviderBase(options), IStationAlertProvider
{
    public override ValueTask<IReadOnlyList<ProviderCapability>> GetCapabilitiesAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<ProviderCapability>>([new("station.alert", true, true, "HTTP JSON POST")]);
    public ValueTask<ProviderOperationResult> AlertAsync(ProviderOperationContext context, StationAlertRequest request, CancellationToken cancellationToken = default) => PostJsonAsync(context, new { context, request }, cancellationToken);
}

public sealed class HttpJsonFireReportingProvider(HttpProviderOptions options) : HttpProviderBase(options), IFireReportingProvider
{
    public override ValueTask<IReadOnlyList<ProviderCapability>> GetCapabilitiesAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<ProviderCapability>>([new("fire-report.submit", true, true, "HTTP JSON POST")]);
    public ValueTask<ProviderValidationResult> ValidateReportAsync(FireReportSubmission report, CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        if (report.ReportId == Guid.Empty) errors.Add("Report ID is required.");
        if (string.IsNullOrWhiteSpace(report.PayloadJson)) errors.Add("Payload is required.");
        else { try { using var _ = JsonDocument.Parse(report.PayloadJson); } catch { errors.Add("Payload must be valid JSON."); } }
        if (string.IsNullOrWhiteSpace(report.SchemaVersion)) errors.Add("Schema version is required.");
        return ValueTask.FromResult(new ProviderValidationResult(errors.Count == 0, errors, []));
    }
    public ValueTask<ProviderOperationResult> SubmitReportAsync(ProviderOperationContext context, FireReportSubmission report, CancellationToken cancellationToken = default) => PostJsonAsync(context, new { context, report }, cancellationToken);
}

public sealed class FileSpoolPrintProvider(string spoolDirectory) : IPrintProvider
{
    private readonly string _spoolDirectory = Path.GetFullPath(spoolDirectory);
    public string Key => "file-spool-print";
    public string DisplayName => "File Spool Printer";
    public string Category => "Printing";
    public ValueTask<ProviderValidationResult> ValidateConfigurationAsync(CancellationToken cancellationToken = default)
    {
        try { Directory.CreateDirectory(_spoolDirectory); return ValueTask.FromResult(new ProviderValidationResult(true, [], ["File-spool printing requires an external OS print watcher for physical output."])); }
        catch (Exception ex) { return ValueTask.FromResult(new ProviderValidationResult(false, [ex.Message], [])); }
    }
    public ValueTask<IReadOnlyList<ProviderCapability>> GetCapabilitiesAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<ProviderCapability>>([new("print.spool", true, true, _spoolDirectory)]);
    public ValueTask<ProviderHealth> GetHealthAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(new ProviderHealth(Key, Directory.Exists(_spoolDirectory) ? ProviderAvailability.Available : ProviderAvailability.Degraded, _spoolDirectory, DateTimeOffset.UtcNow, null));
    public async ValueTask<ProviderOperationResult> PrintAsync(ProviderOperationContext context, PrintJobRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            Directory.CreateDirectory(_spoolDirectory);
            var extension = request.ContentType.Contains("pdf", StringComparison.OrdinalIgnoreCase) ? ".pdf" : ".bin";
            var safePrinter = string.Concat(request.PrinterKey.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_'));
            var path = Path.Combine(_spoolDirectory, $"{DateTime.UtcNow:yyyyMMddHHmmssfff}_{safePrinter}_{context.OperationId:N}_r{request.Revision}{extension}");
            await File.WriteAllBytesAsync(path, request.Content.ToArray(), cancellationToken);
            return new ProviderOperationResult(context.OperationId, "Spooled", path, null, DateTimeOffset.UtcNow);
        }
        catch (Exception ex) { return new ProviderOperationResult(context.OperationId, "Failed", null, ex.Message, DateTimeOffset.UtcNow); }
    }
}
