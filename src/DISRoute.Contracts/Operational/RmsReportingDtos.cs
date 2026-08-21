namespace DISRoute.Contracts.Operational;

public sealed record RmsUnitSnapshotDto(Guid UnitId, Guid StationId, string CallSign, string Discipline, string UnitType, string LastIncidentStatus, DateTimeOffset LastIncidentUpdateUtc);
public sealed record RmsTimelineSnapshotDto(Guid Id, string Category, string Message, string Actor, DateTimeOffset OccurredAtUtc);
public sealed record RmsCommandSnapshotDto(Guid Id, string CommandName, Guid? UnitId, string Person, string Actor, DateTimeOffset EffectiveAtUtc, DateTimeOffset? EndedAtUtc);
public sealed record RmsMutualAidSnapshotDto(Guid Id, string RequestedAgency, string ResourceType, int Quantity, string State, string RequestedBy, DateTimeOffset RequestedAtUtc, DateTimeOffset UpdatedAtUtc);
public sealed record RmsFieldProvenanceDto(Guid Id, string Field, string SourceKind, string SourceReference, string Actor, DateTimeOffset RecordedAtUtc);

public sealed record RmsIncidentReportDto(
    Guid Id, Guid AgencyId, Guid IncidentId, string Number, string ReportType, string Status, int Version,
    string IncidentNumber, long SourceIncidentVersion, DateTimeOffset SourceCapturedAtUtc, Guid StationId,
    string Nature, string Priority, string Address, string CrossStreets, string Municipality, string CallerName, string CallerPhone,
    DateTimeOffset ReceivedAtUtc, DateTimeOffset? ClosedAtUtc,
    IReadOnlyList<RmsUnitSnapshotDto> Units, IReadOnlyList<RmsTimelineSnapshotDto> Timeline,
    IReadOnlyList<RmsCommandSnapshotDto> CommandAssignments, IReadOnlyList<RmsMutualAidSnapshotDto> MutualAid,
    string Narrative, string Summary, string CreatedBy,
    string? ReviewedBy, DateTimeOffset? ReviewedAtUtc, string? ReviewNotes,
    string? FinalizedBy, DateTimeOffset? FinalizedAtUtc,
    IReadOnlyList<RmsFieldProvenanceDto> Provenance, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);

public sealed record RmsReportVersionDto(Guid Id, Guid ReportId, int Version, string Status, string ChangeKind, string Actor, string Reason, string SnapshotJson, DateTimeOffset CreatedAtUtc);
public sealed record RmsAttachmentReferenceDto(Guid Id, Guid ReportId, Guid AgencyId, string FileName, string Classification, string StorageReference, string? Description, string AddedBy, DateTimeOffset AddedAtUtc);
public sealed record RmsValidationResultDto(Guid ReportId, bool CanonicalReady, bool ProviderValidationPending, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings, DateTimeOffset ValidatedAtUtc);

public sealed record CreateRmsFromIncidentRequest(Guid AgencyId, string ReportType, string Summary, string? IdempotencyKey);
public sealed record UpdateRmsIncidentReportRequest(int ExpectedVersion, string? Nature, string? Priority, string? Address, string? CrossStreets, string? Municipality, string? CallerName, string? CallerPhone, string? Narrative, string? Summary, string? IdempotencyKey);
public sealed record RefreshRmsCadSnapshotRequest(int ExpectedVersion, string? IdempotencyKey);
public sealed record SubmitRmsForReviewRequest(int ExpectedVersion, string? Notes, string? IdempotencyKey);
public sealed record ReviewRmsIncidentReportRequest(int ExpectedVersion, string Decision, string? Notes, string? IdempotencyKey);
public sealed record AmendRmsIncidentReportRequest(int ExpectedVersion, string Reason, string? Nature, string? Priority, string? Address, string? CrossStreets, string? Municipality, string? CallerName, string? CallerPhone, string? Narrative, string? Summary, string? IdempotencyKey);
public sealed record AddRmsAttachmentReferenceRequest(string FileName, string Classification, string StorageReference, string? Description, string? IdempotencyKey);

public sealed record NerisSubmissionDto(
    Guid Id, Guid RmsReportId, Guid AgencyId, Guid IncidentId, int RmsReportVersion,
    string ProfileVersion, string State, string PayloadJson, string PayloadSha256,
    IReadOnlyList<string> MappingErrors, IReadOnlyList<string> MappingWarnings,
    string? ProviderKey, string? ProviderReference, int AttemptCount,
    DateTimeOffset? LastAttemptAtUtc, string? LastError,
    string CreatedBy, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);

public sealed record NerisSubmissionAttemptDto(
    Guid Id, Guid SubmissionId, int AttemptNumber, Guid OperationId, string ProviderKey,
    string State, string Actor, IReadOnlyList<string> ValidationErrors, IReadOnlyList<string> ValidationWarnings,
    string? ProviderReference, string? Error, DateTimeOffset RequestedAtUtc, DateTimeOffset? CompletedAtUtc);

public sealed record NerisProviderValidationDto(
    Guid SubmissionId, string ProviderKey, bool Valid, IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings, DateTimeOffset ValidatedAtUtc);

public sealed record BuildNerisSubmissionRequest(string? ProfileVersion, string? IdempotencyKey);
public sealed record QueueNerisSubmissionRequest(string? ProviderKey, string? IdempotencyKey);
public sealed record RetryNerisSubmissionRequest(string? ProviderKey, bool ConfirmPossibleDuplicate, string? IdempotencyKey);
public sealed record ReconcileNerisSubmissionRequest(string Resolution, string? ProviderReference, string? Notes, string? IdempotencyKey);
