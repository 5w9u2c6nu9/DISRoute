namespace DISRoute.Contracts.Operational;

public sealed record DmsDocumentRevisionDto(
    Guid Id,
    Guid DocumentId,
    int Version,
    string Content,
    string State,
    string Author,
    string ContentSha256,
    DateTimeOffset CreatedAtUtc,
    string? PublishedBy,
    DateTimeOffset? PublishedAtUtc);

public sealed record DmsDocumentSummaryDto(
    Guid Id,
    Guid AgencyId,
    string Title,
    string Classification,
    int CurrentVersion,
    string State,
    string CreatedBy,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    int PublishedVersion,
    int ActiveDistributions,
    int RequiredReads,
    int CompletedRequiredReads,
    int OverdueRequiredReads);

public sealed record CreateDmsDocumentRequest(Guid AgencyId, string Title, string Classification, string Content);
public sealed record CreateDmsRevisionRequest(string Content);
public sealed record PublishDmsRevisionRequest(bool RequireApprovedWorkflow = true);

public sealed record DmsDistributionDto(
    Guid Id,
    Guid AgencyId,
    Guid DocumentId,
    int DocumentVersion,
    string ScopeType,
    string ScopeValue,
    bool RequiredRead,
    DateTimeOffset? DueAtUtc,
    string State,
    string AssignedBy,
    DateTimeOffset AssignedAtUtc);

public sealed record CreateDmsDistributionRequest(
    int DocumentVersion,
    string ScopeType,
    string ScopeValue,
    bool RequiredRead,
    DateTimeOffset? DueAtUtc,
    IReadOnlyList<string> Recipients);

public sealed record DmsReadReceiptDto(
    Guid Id,
    Guid DistributionId,
    Guid DocumentId,
    int DocumentVersion,
    string Recipient,
    string State,
    DateTimeOffset AssignedAtUtc,
    DateTimeOffset? ReadAtUtc,
    DateTimeOffset? AcknowledgedAtUtc,
    string? AcknowledgedBy);

public sealed record DmsRequiredReadStatusDto(
    Guid DocumentId,
    int DocumentVersion,
    int Assigned,
    int Read,
    int Acknowledged,
    int Pending,
    int Overdue,
    IReadOnlyList<DmsReadReceiptDto> Receipts);


public sealed record DmsAssetReferenceDto(
    Guid Id,
    Guid AgencyId,
    Guid DocumentId,
    int DocumentVersion,
    string FileName,
    string ContentType,
    string StorageReference,
    string? ContentSha256,
    string Classification,
    string AddedBy,
    DateTimeOffset AddedAtUtc);

public sealed record AddDmsAssetReferenceRequest(int DocumentVersion, string FileName, string ContentType, string StorageReference, string? ContentSha256, string Classification);

public sealed record DmsDocumentLinkDto(
    Guid Id,
    Guid AgencyId,
    Guid DocumentId,
    int DocumentVersion,
    string EntityType,
    string EntityKey,
    string Relationship,
    string LinkedBy,
    DateTimeOffset LinkedAtUtc);

public sealed record CreateDmsDocumentLinkRequest(int DocumentVersion, string EntityType, string EntityKey, string Relationship);

public sealed record DmsApprovalRequest(string Name, string Mode, IReadOnlyList<ApprovalStepDefinition> Steps);

public sealed record FormSignatureDto(
    Guid Id,
    Guid FormId,
    Guid TemplateId,
    int TemplateVersion,
    string Signer,
    string Meaning,
    string ValuesSha256,
    string Source,
    DateTimeOffset SignedAtUtc);

public sealed record SignFormStrongRequest(string Meaning = "Approved / certified by signer");
