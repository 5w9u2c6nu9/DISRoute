using System.Text.Json;
using DISRoute.Contracts.Operational;

namespace DISRoute.Server.Operational;

public sealed partial class OperationalStore
{
    private readonly Dictionary<Guid, RmsIncidentReportDto> _rmsIncidentReports = [];
    private readonly Dictionary<Guid, List<RmsReportVersionDto>> _rmsReportVersions = [];
    private readonly Dictionary<Guid, List<RmsAttachmentReferenceDto>> _rmsAttachments = [];
    private static readonly JsonSerializerOptions RmsSnapshotJsonOptions = new(JsonSerializerDefaults.Web);

    public IReadOnlyList<RmsIncidentReportDto> RmsIncidentReports { get { lock (_gate) return _rmsIncidentReports.Values.OrderByDescending(x => x.UpdatedAtUtc).ToArray(); } }
    public IReadOnlyList<RmsReportVersionDto> RmsReportVersions { get { lock (_gate) return _rmsReportVersions.Values.SelectMany(x => x).OrderBy(x => x.CreatedAtUtc).ToArray(); } }
    public IReadOnlyList<RmsAttachmentReferenceDto> RmsAttachments { get { lock (_gate) return _rmsAttachments.Values.SelectMany(x => x).OrderBy(x => x.AddedAtUtc).ToArray(); } }

    public RmsIncidentReportDto GetRmsIncidentReport(Guid id) { lock (_gate) return RequireRmsIncidentReport(id); }
    public IReadOnlyList<RmsReportVersionDto> GetRmsReportVersions(Guid id) { lock (_gate) { _ = RequireRmsIncidentReport(id); return _rmsReportVersions.GetValueOrDefault(id, []).OrderBy(x => x.Version).ToArray(); } }
    public IReadOnlyList<RmsAttachmentReferenceDto> GetRmsAttachments(Guid id) { lock (_gate) { _ = RequireRmsIncidentReport(id); return _rmsAttachments.GetValueOrDefault(id, []).OrderBy(x => x.AddedAtUtc).ToArray(); } }

    public RmsIncidentReportDto CreateRmsIncidentReport(RmsCadHandoffSeed seed, CreateRmsFromIncidentRequest request, string actor)
    {
        lock (_gate)
        {
            var idempotencyRequest = new { seed.Incident.Id, seed.Incident.Version, Actor = actor, Request = request };
            if (TryIdempotent<RmsIncidentReportDto>(request.IdempotencyKey, idempotencyRequest, out var remembered)) return remembered;
            if (seed.Incident.AgencyId != request.AgencyId) throw new InvalidOperationException("CAD incident and RMS report agency do not match.");
            var reportType = string.IsNullOrWhiteSpace(request.ReportType) ? "FireIncident" : request.ReportType.Trim();
            var existing = _rmsIncidentReports.Values.FirstOrDefault(x => x.AgencyId == request.AgencyId && x.IncidentId == seed.Incident.Id && x.ReportType.Equals(reportType, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                Remember(request.IdempotencyKey, idempotencyRequest, existing);
                return existing;
            }

            var now = DateTimeOffset.UtcNow;
            var sourceRef = FormattableString.Invariant($"incident:{seed.Incident.Id:D}:v{seed.Incident.Version}");
            var provenance = new List<RmsFieldProvenanceDto>();
            foreach (var field in CadScalarFields) provenance.Add(NewProvenance(field, "CAD", sourceRef, actor, now));
            foreach (var field in CadCollectionFields) provenance.Add(NewProvenance(field, "CAD", sourceRef, actor, now));
            if (!string.IsNullOrWhiteSpace(request.Summary)) provenance.Add(NewProvenance("Summary", "RecordsUser", "report:create", actor, now));

            var number = $"RMS-{now:yy}-{_rmsSequence++:000000}";
            var report = new RmsIncidentReportDto(
                Guid.NewGuid(), request.AgencyId, seed.Incident.Id, number, reportType, "Draft", 1,
                seed.Incident.Number, seed.Incident.Version, seed.CapturedAtUtc, seed.Incident.StationId,
                seed.Incident.Nature, seed.Incident.Priority, seed.Incident.Address, seed.Incident.CrossStreets, seed.Incident.Municipality,
                seed.Incident.CallerName, seed.Incident.CallerPhone, seed.Incident.ReceivedAtUtc, seed.Incident.ClosedAtUtc,
                seed.Units, seed.Timeline, seed.Commands, seed.MutualAid, seed.Narrative, request.Summary?.Trim() ?? string.Empty,
                NormalizeRmsActor(actor), null, null, null, null, null, provenance, now, now);
            _rmsIncidentReports[report.Id] = report;
            _rms[report.Id] = new RmsRecordDto(report.Id, report.AgencyId, report.ReportType, report.Number, $"{report.IncidentNumber} — {report.Nature}", report.Status, report.Summary, report.IncidentId, now, now);
            CaptureRmsVersion(report, actor, "CreatedFromCAD", "Initial CAD-to-RMS handoff");
            AddReplay(report.AgencyId, report.IncidentId, "RMS", "IncidentReportCreated", $"{report.Number} from {report.IncidentNumber}", actor);
            Remember(request.IdempotencyKey, idempotencyRequest, report);
            return report;
        }
    }

    public RmsIncidentReportDto UpdateRmsIncidentReport(Guid id, UpdateRmsIncidentReportRequest request, string actor)
    {
        lock (_gate)
        {
            var idempotencyRequest = new { TargetId = id, Actor = actor, Request = request };
            if (TryIdempotent<RmsIncidentReportDto>(request.IdempotencyKey, idempotencyRequest, out var remembered)) return remembered;
            var current = RequireRmsIncidentReport(id);
            EnsureRmsVersion(current, request.ExpectedVersion);
            if (!current.Status.Equals("Draft", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Only Draft RMS reports can be edited directly. Finalized records require an amendment.");
            var now = DateTimeOffset.UtcNow;
            var provenance = current.Provenance.ToList();
            var updated = current with
            {
                Nature = ApplyRmsUserField("Nature", current.Nature, request.Nature, provenance, actor, now),
                Priority = ApplyRmsUserField("Priority", current.Priority, request.Priority, provenance, actor, now),
                Address = ApplyRmsUserField("Address", current.Address, request.Address, provenance, actor, now),
                CrossStreets = ApplyRmsUserField("CrossStreets", current.CrossStreets, request.CrossStreets, provenance, actor, now),
                Municipality = ApplyRmsUserField("Municipality", current.Municipality, request.Municipality, provenance, actor, now),
                CallerName = ApplyRmsUserField("CallerName", current.CallerName, request.CallerName, provenance, actor, now),
                CallerPhone = ApplyRmsUserField("CallerPhone", current.CallerPhone, request.CallerPhone, provenance, actor, now),
                Narrative = ApplyRmsUserField("Narrative", current.Narrative, request.Narrative, provenance, actor, now),
                Summary = ApplyRmsUserField("Summary", current.Summary, request.Summary, provenance, actor, now),
                Version = current.Version + 1, Provenance = provenance, UpdatedAtUtc = now
            };
            EnsureRmsContentChanged(current, updated, "Draft edit did not change any report fields.");
            SaveRmsReport(updated, actor, "DraftEdited", "Draft edited");
            Remember(request.IdempotencyKey, idempotencyRequest, updated);
            return updated;
        }
    }

    public RmsIncidentReportDto RefreshRmsCadSnapshot(Guid id, RmsCadHandoffSeed seed, RefreshRmsCadSnapshotRequest request, string actor)
    {
        lock (_gate)
        {
            var idempotencyRequest = new { TargetId = id, seed.Incident.Version, Actor = actor, Request = request };
            if (TryIdempotent<RmsIncidentReportDto>(request.IdempotencyKey, idempotencyRequest, out var remembered)) return remembered;
            var current = RequireRmsIncidentReport(id);
            EnsureRmsVersion(current, request.ExpectedVersion);
            if (!current.Status.Equals("Draft", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("CAD source refresh is only allowed while the RMS report is Draft.");
            if (seed.Incident.Id != current.IncidentId || seed.Incident.AgencyId != current.AgencyId) throw new InvalidOperationException("CAD source does not match the RMS report.");
            var now = DateTimeOffset.UtcNow;
            var provenance = current.Provenance.ToList();
            var sourceRef = FormattableString.Invariant($"incident:{seed.Incident.Id:D}:v{seed.Incident.Version}");
            var updated = current with
            {
                SourceIncidentVersion = seed.Incident.Version, SourceCapturedAtUtc = seed.CapturedAtUtc, StationId = seed.Incident.StationId,
                Nature = RefreshRmsCadField("Nature", current.Nature, seed.Incident.Nature, provenance, sourceRef, actor, now),
                Priority = RefreshRmsCadField("Priority", current.Priority, seed.Incident.Priority, provenance, sourceRef, actor, now),
                Address = RefreshRmsCadField("Address", current.Address, seed.Incident.Address, provenance, sourceRef, actor, now),
                CrossStreets = RefreshRmsCadField("CrossStreets", current.CrossStreets, seed.Incident.CrossStreets, provenance, sourceRef, actor, now),
                Municipality = RefreshRmsCadField("Municipality", current.Municipality, seed.Incident.Municipality, provenance, sourceRef, actor, now),
                CallerName = RefreshRmsCadField("CallerName", current.CallerName, seed.Incident.CallerName, provenance, sourceRef, actor, now),
                CallerPhone = RefreshRmsCadField("CallerPhone", current.CallerPhone, seed.Incident.CallerPhone, provenance, sourceRef, actor, now),
                ClosedAtUtc = seed.Incident.ClosedAtUtc,
                Units = seed.Units, Timeline = seed.Timeline, CommandAssignments = seed.Commands, MutualAid = seed.MutualAid,
                Narrative = RefreshRmsCadField("Narrative", current.Narrative, seed.Narrative, provenance, sourceRef, actor, now),
                Version = current.Version + 1, Provenance = RefreshRmsCollectionProvenance(provenance, sourceRef, actor, now), UpdatedAtUtc = now
            };
            SaveRmsReport(updated, actor, "CadRefresh", FormattableString.Invariant($"CAD snapshot refreshed from incident version {seed.Incident.Version}"));
            Remember(request.IdempotencyKey, idempotencyRequest, updated);
            return updated;
        }
    }

    public RmsIncidentReportDto SubmitRmsForReview(Guid id, SubmitRmsForReviewRequest request, string actor)
    {
        lock (_gate)
        {
            var idempotencyRequest = new { TargetId = id, Actor = actor, Request = request };
            if (TryIdempotent<RmsIncidentReportDto>(request.IdempotencyKey, idempotencyRequest, out var remembered)) return remembered;
            var current = RequireRmsIncidentReport(id); EnsureRmsVersion(current, request.ExpectedVersion);
            if (!current.Status.Equals("Draft", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Only Draft RMS reports can be submitted for review.");
            var validation = ValidateRmsIncidentReportInternal(current);
            if (validation.Errors.Count > 0) throw new InvalidOperationException("RMS report cannot enter review: " + string.Join("; ", validation.Errors));
            var updated = current with { Status = "InReview", Version = current.Version + 1, ReviewNotes = NormalizeRmsOptional(request.Notes), UpdatedAtUtc = DateTimeOffset.UtcNow };
            SaveRmsReport(updated, actor, "SubmittedForReview", "Submitted for review");
            Remember(request.IdempotencyKey, idempotencyRequest, updated);
            return updated;
        }
    }

    public RmsIncidentReportDto ReviewRmsIncidentReport(Guid id, ReviewRmsIncidentReportRequest request, string actor)
    {
        lock (_gate)
        {
            var idempotencyRequest = new { TargetId = id, Actor = actor, Request = request };
            if (TryIdempotent<RmsIncidentReportDto>(request.IdempotencyKey, idempotencyRequest, out var remembered)) return remembered;
            var current = RequireRmsIncidentReport(id); EnsureRmsVersion(current, request.ExpectedVersion);
            if (!current.Status.Equals("InReview", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("RMS report is not currently in review.");
            var decision = request.Decision?.Trim().ToLowerInvariant();
            var now = DateTimeOffset.UtcNow;
            RmsIncidentReportDto updated;
            if (decision is "approve" or "finalize")
            {
                var validation = ValidateRmsIncidentReportInternal(current);
                if (validation.Errors.Count > 0) throw new InvalidOperationException("RMS report cannot be finalized: " + string.Join("; ", validation.Errors));
                if (current.ClosedAtUtc is null) throw new InvalidOperationException("RMS report cannot be finalized until the CAD incident has a closed timestamp. Refresh the CAD snapshot after incident closure.");
                updated = current with { Status = "Finalized", Version = current.Version + 1, ReviewedBy = NormalizeRmsActor(actor), ReviewedAtUtc = now, ReviewNotes = NormalizeRmsOptional(request.Notes), FinalizedBy = NormalizeRmsActor(actor), FinalizedAtUtc = now, UpdatedAtUtc = now };
                SaveRmsReport(updated, actor, "Finalized", "Reviewed and finalized");
            }
            else if (decision is "return" or "returntodraft" or "reject")
            {
                updated = current with { Status = "Draft", Version = current.Version + 1, ReviewedBy = NormalizeRmsActor(actor), ReviewedAtUtc = now, ReviewNotes = NormalizeRmsOptional(request.Notes), UpdatedAtUtc = now };
                SaveRmsReport(updated, actor, "ReturnedToDraft", "Review returned to Draft");
            }
            else throw new ArgumentException("Review decision must be approve or return.");
            Remember(request.IdempotencyKey, idempotencyRequest, updated);
            return updated;
        }
    }

    public RmsIncidentReportDto AmendRmsIncidentReport(Guid id, AmendRmsIncidentReportRequest request, string actor)
    {
        lock (_gate)
        {
            var idempotencyRequest = new { TargetId = id, Actor = actor, Request = request };
            if (TryIdempotent<RmsIncidentReportDto>(request.IdempotencyKey, idempotencyRequest, out var remembered)) return remembered;
            var current = RequireRmsIncidentReport(id); EnsureRmsVersion(current, request.ExpectedVersion);
            if (!current.Status.Equals("Finalized", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Only Finalized RMS reports use the amendment workflow.");
            if (string.IsNullOrWhiteSpace(request.Reason)) throw new ArgumentException("Amendment reason is required.");
            var now = DateTimeOffset.UtcNow;
            var provenance = current.Provenance.ToList();
            var updated = current with
            {
                Nature = ApplyRmsAmendmentField("Nature", current.Nature, request.Nature, provenance, actor, now),
                Priority = ApplyRmsAmendmentField("Priority", current.Priority, request.Priority, provenance, actor, now),
                Address = ApplyRmsAmendmentField("Address", current.Address, request.Address, provenance, actor, now),
                CrossStreets = ApplyRmsAmendmentField("CrossStreets", current.CrossStreets, request.CrossStreets, provenance, actor, now),
                Municipality = ApplyRmsAmendmentField("Municipality", current.Municipality, request.Municipality, provenance, actor, now),
                CallerName = ApplyRmsAmendmentField("CallerName", current.CallerName, request.CallerName, provenance, actor, now),
                CallerPhone = ApplyRmsAmendmentField("CallerPhone", current.CallerPhone, request.CallerPhone, provenance, actor, now),
                Narrative = ApplyRmsAmendmentField("Narrative", current.Narrative, request.Narrative, provenance, actor, now),
                Summary = ApplyRmsAmendmentField("Summary", current.Summary, request.Summary, provenance, actor, now),
                Version = current.Version + 1, Provenance = provenance, UpdatedAtUtc = now
            };
            EnsureRmsContentChanged(current, updated, "Amendment must change at least one report field.");
            SaveRmsReport(updated, actor, "Amendment", request.Reason.Trim());
            Remember(request.IdempotencyKey, idempotencyRequest, updated);
            return updated;
        }
    }

    public RmsAttachmentReferenceDto AddRmsAttachmentReference(Guid reportId, AddRmsAttachmentReferenceRequest request, string actor)
    {
        lock (_gate)
        {
            var idempotencyRequest = new { TargetId = reportId, Actor = actor, Request = request };
            if (TryIdempotent<RmsAttachmentReferenceDto>(request.IdempotencyKey, idempotencyRequest, out var remembered)) return remembered;
            var report = RequireRmsIncidentReport(reportId);
            if (string.IsNullOrWhiteSpace(request.FileName) || string.IsNullOrWhiteSpace(request.StorageReference) || string.IsNullOrWhiteSpace(request.Classification)) throw new ArgumentException("File name, classification and storage reference are required.");
            var existing = _rmsAttachments.GetValueOrDefault(reportId, []).FirstOrDefault(x => x.StorageReference.Equals(request.StorageReference.Trim(), StringComparison.OrdinalIgnoreCase));
            if (existing is not null) return existing;
            var item = new RmsAttachmentReferenceDto(Guid.NewGuid(), reportId, report.AgencyId, request.FileName.Trim(), request.Classification.Trim(), request.StorageReference.Trim(), NormalizeRmsOptional(request.Description), NormalizeRmsActor(actor), DateTimeOffset.UtcNow);
            if (!_rmsAttachments.TryGetValue(reportId, out var list)) _rmsAttachments[reportId] = list = [];
            list.Add(item);
            AddReplay(report.AgencyId, report.IncidentId, "RMS", "AttachmentReferenced", item.FileName, actor);
            Remember(request.IdempotencyKey, idempotencyRequest, item);
            return item;
        }
    }

    public RmsValidationResultDto ValidateRmsIncidentReport(Guid id)
    {
        lock (_gate)
        {
            var report = RequireRmsIncidentReport(id);
            var canonical = ValidateRmsIncidentReportInternal(report);
            var currentSubmissions = _nerisSubmissions.Values.Where(x => x.RmsReportId == report.Id && x.RmsReportVersion == report.Version).ToArray();
            var providerValidated = currentSubmissions.Any(x => _nerisSubmissionAttempts.GetValueOrDefault(x.Id, []).Any(a => a.CompletedAtUtc is not null && a.ValidationErrors.Count == 0 && a.State is not ("ProviderValidationFailed" or "UnknownAfterRestore")));
            var warnings = canonical.Warnings.ToList();
            if (!providerValidated) warnings.Add("Provider-specific NERIS validation has not yet been performed for the current RMS version.");
            return canonical with { ProviderValidationPending = !providerValidated, Warnings = warnings };
        }
    }

    internal static RmsValidationResultDto ValidateRmsIncidentReportInternal(RmsIncidentReportDto report)
    {
        var errors = new List<string>(); var warnings = new List<string>();
        if (string.IsNullOrWhiteSpace(report.IncidentNumber)) errors.Add("Incident number is required.");
        if (string.IsNullOrWhiteSpace(report.Nature)) errors.Add("Incident nature is required.");
        if (string.IsNullOrWhiteSpace(report.Address)) errors.Add("Incident address is required.");
        if (string.IsNullOrWhiteSpace(report.Municipality)) errors.Add("Municipality is required.");
        if (report.Units.Count == 0) warnings.Add("No responding units are captured.");
        if (string.IsNullOrWhiteSpace(report.Narrative)) warnings.Add("Narrative is empty.");
        if (report.ClosedAtUtc is null) warnings.Add("CAD incident is still open; finalization is not yet available.");
        return new RmsValidationResultDto(report.Id, errors.Count == 0 && report.ClosedAtUtc is not null, true, errors, warnings, DateTimeOffset.UtcNow);
    }

    internal void ImportRmsReportingState(IEnumerable<RmsIncidentReportDto>? reports, IEnumerable<RmsReportVersionDto>? versions, IEnumerable<RmsAttachmentReferenceDto>? attachments)
    {
        _rmsIncidentReports.Clear(); foreach (var x in reports ?? []) _rmsIncidentReports[x.Id] = x;
        _rmsReportVersions.Clear(); foreach (var x in versions ?? []) { if (!_rmsReportVersions.TryGetValue(x.ReportId, out var list)) _rmsReportVersions[x.ReportId] = list = []; list.Add(x); }
        _rmsAttachments.Clear(); foreach (var x in attachments ?? []) { if (!_rmsAttachments.TryGetValue(x.ReportId, out var list)) _rmsAttachments[x.ReportId] = list = []; list.Add(x); }
    }

    private void SaveRmsReport(RmsIncidentReportDto report, string actor, string changeKind, string reason)
    {
        _rmsIncidentReports[report.Id] = report;
        _rms[report.Id] = new RmsRecordDto(report.Id, report.AgencyId, report.ReportType, report.Number, $"{report.IncidentNumber} — {report.Nature}", report.Status, report.Summary, report.IncidentId, report.CreatedAtUtc, report.UpdatedAtUtc);
        CaptureRmsVersion(report, actor, changeKind, reason);
        AddReplay(report.AgencyId, report.IncidentId, "RMS", changeKind, $"{report.Number} v{report.Version}: {reason}", actor);
    }

    private void CaptureRmsVersion(RmsIncidentReportDto report, string actor, string changeKind, string reason)
    {
        var item = new RmsReportVersionDto(Guid.NewGuid(), report.Id, report.Version, report.Status, changeKind, NormalizeRmsActor(actor), reason, JsonSerializer.Serialize(report, RmsSnapshotJsonOptions), DateTimeOffset.UtcNow);
        if (!_rmsReportVersions.TryGetValue(report.Id, out var list)) _rmsReportVersions[report.Id] = list = [];
        list.Add(item);
    }

    private RmsIncidentReportDto RequireRmsIncidentReport(Guid id) => _rmsIncidentReports.TryGetValue(id, out var report) ? report : throw new KeyNotFoundException("RMS incident report not found.");
    private static void EnsureRmsVersion(RmsIncidentReportDto report, int expected) { if (report.Version != expected) throw new InvalidOperationException($"RMS report version conflict. Expected {expected}; current {report.Version}."); }
    private static string NormalizeRmsActor(string actor) => string.IsNullOrWhiteSpace(actor) ? "unknown" : actor.Trim();
    private static string? NormalizeRmsOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static RmsFieldProvenanceDto NewProvenance(string field, string sourceKind, string sourceReference, string actor, DateTimeOffset now) => new(Guid.NewGuid(), field, sourceKind, sourceReference, NormalizeRmsActor(actor), now);

    private static string ApplyRmsUserField(string field, string current, string? requested, List<RmsFieldProvenanceDto> provenance, string actor, DateTimeOffset now)
    {
        if (requested is null || requested == current) return current;
        provenance.Add(NewProvenance(field, "RecordsUser", "report:edit", actor, now)); return requested.Trim();
    }
    private static string ApplyRmsAmendmentField(string field, string current, string? requested, List<RmsFieldProvenanceDto> provenance, string actor, DateTimeOffset now)
    {
        if (requested is null || requested == current) return current;
        provenance.Add(NewProvenance(field, "RmsAmendment", "report:amendment", actor, now)); return requested.Trim();
    }
    private static string RefreshRmsCadField(string field, string current, string cadValue, List<RmsFieldProvenanceDto> provenance, string sourceRef, string actor, DateTimeOffset now)
    {
        var latest = provenance.Where(x => x.Field.Equals(field, StringComparison.OrdinalIgnoreCase)).OrderByDescending(x => x.RecordedAtUtc).FirstOrDefault();
        if (latest is not null && !latest.SourceKind.Equals("CAD", StringComparison.OrdinalIgnoreCase)) return current;
        if (current != cadValue || latest is null || latest.SourceReference != sourceRef) provenance.Add(NewProvenance(field, "CAD", sourceRef, actor, now));
        return cadValue;
    }
    private static List<RmsFieldProvenanceDto> RefreshRmsCollectionProvenance(List<RmsFieldProvenanceDto> provenance, string sourceRef, string actor, DateTimeOffset now)
    {
        foreach (var field in CadCollectionFields) provenance.Add(NewProvenance(field, "CAD", sourceRef, actor, now));
        return provenance;
    }
    private static void EnsureRmsContentChanged(RmsIncidentReportDto current, RmsIncidentReportDto updated, string message)
    {
        if (current.Nature == updated.Nature && current.Priority == updated.Priority && current.Address == updated.Address && current.CrossStreets == updated.CrossStreets && current.Municipality == updated.Municipality && current.CallerName == updated.CallerName && current.CallerPhone == updated.CallerPhone && current.Narrative == updated.Narrative && current.Summary == updated.Summary) throw new InvalidOperationException(message);
    }

    private static readonly string[] CadScalarFields = ["IncidentNumber", "Nature", "Priority", "Address", "CrossStreets", "Municipality", "CallerName", "CallerPhone", "ReceivedAtUtc", "ClosedAtUtc", "Narrative"];
    private static readonly string[] CadCollectionFields = ["Units", "Timeline", "CommandAssignments", "MutualAid"];
}
