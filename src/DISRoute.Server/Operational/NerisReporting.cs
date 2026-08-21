using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DISRoute.Contracts.Operational;

namespace DISRoute.Server.Operational;

public sealed partial class OperationalStore
{
    public const string DefaultNerisProfileVersion = "DISRoute-NERIS-Bridge-2026.1";

    private readonly Dictionary<Guid, NerisSubmissionDto> _nerisSubmissions = [];
    private readonly Dictionary<Guid, List<NerisSubmissionAttemptDto>> _nerisSubmissionAttempts = [];
    private static readonly JsonSerializerOptions NerisJsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    public IReadOnlyList<NerisSubmissionDto> NerisSubmissions
    {
        get { lock (_gate) return _nerisSubmissions.Values.OrderByDescending(x => x.UpdatedAtUtc).ToArray(); }
    }

    public NerisSubmissionDto GetNerisSubmission(Guid id)
    {
        lock (_gate) return RequireNerisSubmission(id);
    }

    public IReadOnlyList<NerisSubmissionAttemptDto> GetNerisSubmissionAttempts(Guid id)
    {
        lock (_gate)
        {
            _ = RequireNerisSubmission(id);
            return _nerisSubmissionAttempts.GetValueOrDefault(id, []).OrderBy(x => x.AttemptNumber).ToArray();
        }
    }

    public NerisSubmissionDto BuildNerisSubmission(Guid rmsReportId, BuildNerisSubmissionRequest request, string actor)
    {
        lock (_gate)
        {
            var idempotencyRequest = new { TargetId = rmsReportId, Actor = actor, Request = request };
            if (TryIdempotent<NerisSubmissionDto>(request.IdempotencyKey, idempotencyRequest, out var remembered)) return remembered;

            var report = RequireRmsIncidentReport(rmsReportId);
            if (!report.Status.Equals("Finalized", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("NERIS projection requires a Finalized RMS report.");

            var profile = string.IsNullOrWhiteSpace(request.ProfileVersion) ? DefaultNerisProfileVersion : request.ProfileVersion.Trim();
            if (!profile.Equals(DefaultNerisProfileVersion, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Unsupported NERIS bridge profile '{profile}'. Supported profile: {DefaultNerisProfileVersion}.");

            var existing = _nerisSubmissions.Values.FirstOrDefault(x => x.RmsReportId == rmsReportId && x.RmsReportVersion == report.Version && x.ProfileVersion.Equals(profile, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                Remember(request.IdempotencyKey, idempotencyRequest, existing);
                return existing;
            }

            foreach (var prior in _nerisSubmissions.Values.Where(x => x.RmsReportId == rmsReportId && x.RmsReportVersion != report.Version && !IsNerisTerminalState(x.State)).ToArray())
                _nerisSubmissions[prior.Id] = prior with { State = "Superseded", LastError = "A newer finalized RMS report version exists.", UpdatedAtUtc = DateTimeOffset.UtcNow };

            var attachments = _rmsAttachments.GetValueOrDefault(rmsReportId, []);
            var validation = ValidateNerisMapping(report);
            var payload = BuildNerisPayload(report, attachments, profile);
            var payloadJson = JsonSerializer.Serialize(payload, NerisJsonOptions);
            var payloadSha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson)));
            var now = DateTimeOffset.UtcNow;
            var state = validation.Errors.Count == 0 ? "Ready" : "MappingInvalid";
            var submission = new NerisSubmissionDto(
                Guid.NewGuid(), report.Id, report.AgencyId, report.IncidentId, report.Version,
                profile, state, payloadJson, payloadSha256, validation.Errors, validation.Warnings,
                null, null, 0, null, null, NormalizeRmsActor(actor), now, now);
            _nerisSubmissions[submission.Id] = submission;
            AddReplay(report.AgencyId, report.IncidentId, "NERIS", "ProjectionBuilt", $"{report.Number} v{report.Version} -> {profile} [{state}]", actor);
            Remember(request.IdempotencyKey, idempotencyRequest, submission);
            return submission;
        }
    }

    public NerisSubmissionDto QueueNerisSubmission(Guid id, QueueNerisSubmissionRequest request, string actor)
    {
        lock (_gate)
        {
            var idempotencyRequest = new { TargetId = id, Actor = actor, Request = request };
            if (TryIdempotent<NerisSubmissionDto>(request.IdempotencyKey, idempotencyRequest, out var remembered)) return remembered;
            var current = RequireNerisSubmission(id);
            EnsureNerisSubmissionCurrent(current);
            if (current.MappingErrors.Count > 0) throw new InvalidOperationException("NERIS projection has mapping errors and cannot be queued.");
            if (current.State is not ("Ready" or "Failed")) throw new InvalidOperationException($"NERIS submission in state '{current.State}' cannot be queued.");
            var provider = NormalizeOptionalProvider(request.ProviderKey) ?? current.ProviderKey;
            var updated = current with { State = "Queued", ProviderKey = provider, LastError = null, UpdatedAtUtc = DateTimeOffset.UtcNow };
            _nerisSubmissions[id] = updated;
            AddReplay(updated.AgencyId, updated.IncidentId, "NERIS", "SubmissionQueued", $"NERIS projection {updated.Id:D} queued for {provider ?? "configured provider"}", actor);
            Remember(request.IdempotencyKey, idempotencyRequest, updated);
            return updated;
        }
    }

    public NerisSubmissionDto RetryNerisSubmission(Guid id, RetryNerisSubmissionRequest request, string actor)
    {
        lock (_gate)
        {
            var idempotencyRequest = new { TargetId = id, Actor = actor, Request = request };
            if (TryIdempotent<NerisSubmissionDto>(request.IdempotencyKey, idempotencyRequest, out var remembered)) return remembered;
            var current = RequireNerisSubmission(id);
            EnsureNerisSubmissionCurrent(current);
            if (current.State == "NeedsReconciliation" && !request.ConfirmPossibleDuplicate)
                throw new InvalidOperationException("The previous NERIS attempt may have reached the provider. Reconcile it first or explicitly confirm the possible-duplicate risk before retrying.");
            if (current.State is not ("Failed" or "NeedsReconciliation"))
                throw new InvalidOperationException($"NERIS submission in state '{current.State}' cannot be retried.");
            var provider = NormalizeOptionalProvider(request.ProviderKey) ?? current.ProviderKey;
            var updated = current with { State = "Queued", ProviderKey = provider, LastError = null, UpdatedAtUtc = DateTimeOffset.UtcNow };
            _nerisSubmissions[id] = updated;
            AddReplay(updated.AgencyId, updated.IncidentId, "NERIS", "SubmissionRetryQueued", $"NERIS projection {updated.Id:D} retry queued", actor);
            Remember(request.IdempotencyKey, idempotencyRequest, updated);
            return updated;
        }
    }

    public NerisSubmissionDto ReconcileNerisSubmission(Guid id, ReconcileNerisSubmissionRequest request, string actor)
    {
        lock (_gate)
        {
            var idempotencyRequest = new { TargetId = id, Actor = actor, Request = request };
            if (TryIdempotent<NerisSubmissionDto>(request.IdempotencyKey, idempotencyRequest, out var remembered)) return remembered;
            var current = RequireNerisSubmission(id);
            if (current.State != "NeedsReconciliation") throw new InvalidOperationException("Only a NeedsReconciliation submission can be reconciled manually.");
            var resolution = request.Resolution?.Trim().ToLowerInvariant();
            NerisSubmissionDto updated;
            if (resolution is "confirmed" or "accepted")
            {
                if (string.IsNullOrWhiteSpace(request.ProviderReference)) throw new ArgumentException("Provider reference is required when reconciling as confirmed/accepted.");
                updated = current with { State = resolution == "confirmed" ? "Confirmed" : "Accepted", ProviderReference = request.ProviderReference.Trim(), LastError = NormalizeRmsOptional(request.Notes), UpdatedAtUtc = DateTimeOffset.UtcNow };
            }
            else if (resolution is "failed" or "notreceived")
            {
                updated = current with { State = "Failed", LastError = NormalizeRmsOptional(request.Notes) ?? "Operator reconciled the in-flight attempt as not received by the provider.", UpdatedAtUtc = DateTimeOffset.UtcNow };
            }
            else throw new ArgumentException("Reconciliation resolution must be confirmed, accepted, failed, or notreceived.");
            _nerisSubmissions[id] = updated;
            AddReplay(updated.AgencyId, updated.IncidentId, "NERIS", "SubmissionReconciled", $"NERIS projection {updated.Id:D} reconciled as {updated.State}", actor);
            Remember(request.IdempotencyKey, idempotencyRequest, updated);
            return updated;
        }
    }

    internal (NerisSubmissionDto Submission, NerisSubmissionAttemptDto Attempt) BeginNerisSubmissionAttempt(Guid id, string providerKey, string actor)
    {
        lock (_gate)
        {
            var current = RequireNerisSubmission(id);
            EnsureNerisSubmissionCurrent(current);
            if (current.State != "Queued") throw new InvalidOperationException("NERIS submission must be Queued before transport begins.");
            var now = DateTimeOffset.UtcNow;
            var attemptNumber = current.AttemptCount + 1;
            var attempt = new NerisSubmissionAttemptDto(Guid.NewGuid(), id, attemptNumber, Guid.NewGuid(), providerKey, "Started", NormalizeRmsActor(actor), [], [], null, null, now, null);
            if (!_nerisSubmissionAttempts.TryGetValue(id, out var attempts)) _nerisSubmissionAttempts[id] = attempts = [];
            attempts.Add(attempt);
            var updated = current with { State = "Submitting", ProviderKey = providerKey, AttemptCount = attemptNumber, LastAttemptAtUtc = now, LastError = null, UpdatedAtUtc = now };
            _nerisSubmissions[id] = updated;
            AddReplay(updated.AgencyId, updated.IncidentId, "NERIS", "SubmissionStarted", $"NERIS attempt {attemptNumber} started via {providerKey}", actor);
            return (updated, attempt);
        }
    }

    internal NerisSubmissionDto CompleteNerisProviderValidationFailure(Guid submissionId, Guid attemptId, IReadOnlyList<string> errors, IReadOnlyList<string> warnings, string actor)
    {
        lock (_gate)
        {
            var current = RequireNerisSubmission(submissionId);
            UpdateNerisAttempt(submissionId, attemptId, "ProviderValidationFailed", errors, warnings, null, string.Join("; ", errors));
            var updated = current with { State = "Failed", LastError = "Provider validation failed: " + string.Join("; ", errors), UpdatedAtUtc = DateTimeOffset.UtcNow };
            _nerisSubmissions[submissionId] = updated;
            AddReplay(updated.AgencyId, updated.IncidentId, "NERIS", "ProviderValidationFailed", updated.LastError, actor);
            return updated;
        }
    }

    internal NerisSubmissionDto CompleteNerisProviderAttempt(Guid submissionId, Guid attemptId, string state, string? providerReference, string? error, IReadOnlyList<string> warnings, string actor)
    {
        lock (_gate)
        {
            var current = RequireNerisSubmission(submissionId);
            var providerAccepted = state is "Accepted" or "Confirmed";
            var normalized = providerAccepted
                ? (string.IsNullOrWhiteSpace(providerReference) ? "NeedsReconciliation" : state)
                : "Failed";
            var normalizedError = normalized switch
            {
                "Failed" => string.IsNullOrWhiteSpace(error) ? $"Provider returned state '{state}'." : error,
                "NeedsReconciliation" => "Provider reported success without a durable provider reference. Reconcile before retrying or treating the filing as complete.",
                _ => null
            };
            UpdateNerisAttempt(submissionId, attemptId, normalized, [], warnings, providerReference, normalizedError);
            var updated = current with { State = normalized, ProviderReference = string.IsNullOrWhiteSpace(providerReference) ? null : providerReference, LastError = normalizedError, UpdatedAtUtc = DateTimeOffset.UtcNow };
            _nerisSubmissions[submissionId] = updated;
            AddReplay(updated.AgencyId, updated.IncidentId, "NERIS", normalized == "Failed" ? "SubmissionFailed" : normalized == "NeedsReconciliation" ? "SubmissionNeedsReconciliation" : "SubmissionAccepted", $"NERIS attempt {current.AttemptCount} {normalized}: {providerReference ?? error ?? state}", actor);
            return updated;
        }
    }

    internal void MarkInFlightNerisForReconciliation()
    {
        lock (_gate)
        {
            foreach (var item in _nerisSubmissions.Values.Where(x => x.State == "Submitting").ToArray())
                _nerisSubmissions[item.Id] = item with { State = "NeedsReconciliation", LastError = "DISRoute restored state while this submission was in flight. Provider receipt is unknown; do not retry automatically.", UpdatedAtUtc = DateTimeOffset.UtcNow };
            foreach (var pair in _nerisSubmissionAttempts)
            {
                for (var i = 0; i < pair.Value.Count; i++)
                {
                    var attempt = pair.Value[i];
                    if (attempt.State == "Started") pair.Value[i] = attempt with { State = "UnknownAfterRestore", Error = "Provider receipt unknown after state restore.", CompletedAtUtc = DateTimeOffset.UtcNow };
                }
            }
        }
    }

    internal void ImportNerisSubmissionState(IEnumerable<NerisSubmissionDto>? submissions, IEnumerable<NerisSubmissionAttemptDto>? attempts)
    {
        _nerisSubmissions.Clear(); foreach (var item in submissions ?? []) _nerisSubmissions[item.Id] = item;
        _nerisSubmissionAttempts.Clear();
        foreach (var item in attempts ?? [])
        {
            if (!_nerisSubmissionAttempts.TryGetValue(item.SubmissionId, out var list)) _nerisSubmissionAttempts[item.SubmissionId] = list = [];
            list.Add(item);
        }
        foreach (var list in _nerisSubmissionAttempts.Values) list.Sort((a, b) => a.AttemptNumber.CompareTo(b.AttemptNumber));
        MarkInFlightNerisForReconciliation();
    }

    private NerisSubmissionDto RequireNerisSubmission(Guid id) => _nerisSubmissions.TryGetValue(id, out var item) ? item : throw new KeyNotFoundException("NERIS submission not found.");

    private void EnsureNerisSubmissionCurrent(NerisSubmissionDto submission)
    {
        var report = RequireRmsIncidentReport(submission.RmsReportId);
        if (report.Version != submission.RmsReportVersion)
            throw new InvalidOperationException($"NERIS projection is for RMS v{submission.RmsReportVersion}; current RMS version is v{report.Version}. Build a new projection.");
        if (!report.Status.Equals("Finalized", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("NERIS submission requires a Finalized RMS report.");
    }

    private void UpdateNerisAttempt(Guid submissionId, Guid attemptId, string state, IReadOnlyList<string> errors, IReadOnlyList<string> warnings, string? providerReference, string? error)
    {
        if (!_nerisSubmissionAttempts.TryGetValue(submissionId, out var attempts)) throw new KeyNotFoundException("NERIS submission attempt not found.");
        var index = attempts.FindIndex(x => x.Id == attemptId);
        if (index < 0) throw new KeyNotFoundException("NERIS submission attempt not found.");
        attempts[index] = attempts[index] with { State = state, ValidationErrors = errors, ValidationWarnings = warnings, ProviderReference = providerReference, Error = error, CompletedAtUtc = DateTimeOffset.UtcNow };
    }

    private static (IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings) ValidateNerisMapping(RmsIncidentReportDto report)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        if (!report.Status.Equals("Finalized", StringComparison.OrdinalIgnoreCase)) errors.Add("RMS report must be Finalized.");
        if (report.FinalizedAtUtc is null) errors.Add("RMS finalization timestamp is required.");
        if (report.ClosedAtUtc is null) errors.Add("Incident closed timestamp is required.");
        if (string.IsNullOrWhiteSpace(report.IncidentNumber)) errors.Add("Incident number is required.");
        if (string.IsNullOrWhiteSpace(report.Nature)) errors.Add("Incident nature is required.");
        if (string.IsNullOrWhiteSpace(report.Address)) errors.Add("Incident address is required.");
        if (string.IsNullOrWhiteSpace(report.Municipality)) errors.Add("Municipality is required.");
        if (report.Units.Count == 0) warnings.Add("No responding units are present in the RMS snapshot.");
        if (string.IsNullOrWhiteSpace(report.Narrative)) warnings.Add("Narrative is empty.");
        if (string.IsNullOrWhiteSpace(report.CallerPhone)) warnings.Add("Caller phone is empty.");
        warnings.Add("This payload uses the DISRoute NERIS bridge profile and still requires provider/schema acceptance testing before production certification.");
        return (errors, warnings);
    }

    private static object BuildNerisPayload(RmsIncidentReportDto report, IReadOnlyList<RmsAttachmentReferenceDto> attachments, string profileVersion) => new
    {
        profile = new { name = "DISRoute NERIS Bridge", version = profileVersion },
        source = new
        {
            system = "DISRoute",
            rmsReportId = report.Id,
            rmsReportNumber = report.Number,
            rmsReportVersion = report.Version,
            agencyId = report.AgencyId,
            incidentId = report.IncidentId,
            finalizedBy = report.FinalizedBy,
            finalizedAtUtc = report.FinalizedAtUtc
        },
        incident = new
        {
            number = report.IncidentNumber,
            type = report.ReportType,
            nature = report.Nature,
            priority = report.Priority,
            stationId = report.StationId,
            receivedAtUtc = report.ReceivedAtUtc,
            closedAtUtc = report.ClosedAtUtc,
            location = new { address = report.Address, crossStreets = report.CrossStreets, municipality = report.Municipality },
            caller = new { name = report.CallerName, phone = report.CallerPhone }
        },
        units = report.Units.OrderBy(x => x.CallSign).Select(x => new { x.UnitId, x.StationId, x.CallSign, x.Discipline, x.UnitType, x.LastIncidentStatus, x.LastIncidentUpdateUtc }).ToArray(),
        command = report.CommandAssignments.OrderBy(x => x.EffectiveAtUtc).Select(x => new { x.CommandName, x.UnitId, x.Person, x.EffectiveAtUtc, x.EndedAtUtc }).ToArray(),
        mutualAid = report.MutualAid.OrderBy(x => x.RequestedAtUtc).Select(x => new { x.RequestedAgency, x.ResourceType, x.Quantity, x.State, x.RequestedAtUtc, x.UpdatedAtUtc }).ToArray(),
        timeline = report.Timeline.OrderBy(x => x.OccurredAtUtc).Select(x => new { x.Category, x.Message, x.Actor, x.OccurredAtUtc }).ToArray(),
        report = new { report.Summary, report.Narrative },
        attachments = attachments.OrderBy(x => x.AddedAtUtc).Select(x => new { x.FileName, x.Classification, x.StorageReference, x.Description, x.AddedAtUtc }).ToArray()
    };

    private static bool IsNerisTerminalState(string state) => state is "Accepted" or "Confirmed" or "Superseded";
    private static string? NormalizeOptionalProvider(string? provider) => string.IsNullOrWhiteSpace(provider) ? null : provider.Trim();
}
