using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DISRoute.Contracts.Operational;

namespace DISRoute.Server.Operational;

public sealed partial class FeatureStore
{
    private readonly Dictionary<Guid, DmsDocumentRevisionDto> _dmsRevisions = [];
    private readonly Dictionary<Guid, DmsDistributionDto> _dmsDistributions = [];
    private readonly Dictionary<Guid, DmsReadReceiptDto> _dmsReadReceipts = [];
    private readonly Dictionary<Guid, DmsDocumentLinkDto> _dmsDocumentLinks = [];
    private readonly Dictionary<Guid, DmsAssetReferenceDto> _dmsAssetReferences = [];
    private readonly Dictionary<Guid, FormSignatureDto> _formSignatures = [];

    public IReadOnlyList<DmsDocumentSummaryDto> GetDmsDocuments(Guid? agencyId = null)
    {
        lock (_gate)
        {
            return _core.Documents
                .Where(x => agencyId is null || x.AgencyId == agencyId)
                .Select(BuildDmsSummary)
                .OrderBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public DmsDocumentSummaryDto CreateDmsDocument(CreateDmsDocumentRequest request, string actor)
    {
        lock (_gate)
        {
            if (request.AgencyId == Guid.Empty) throw new ArgumentException("Agency is required.");
            if (string.IsNullOrWhiteSpace(request.Title)) throw new ArgumentException("Document title is required.");
            if (string.IsNullOrWhiteSpace(request.Classification)) throw new ArgumentException("Document classification is required.");
            var document = _core.CreateDocument(new CreateDocumentRequest(request.AgencyId, request.Title.Trim(), request.Classification.Trim(), request.Content ?? string.Empty, actor));
            var revision = new DmsDocumentRevisionDto(Guid.NewGuid(), document.Id, 1, document.Content, "Draft", actor, Sha256(document.Content), document.CreatedAtUtc, null, null);
            _dmsRevisions[revision.Id] = revision;
            EnsureLegacyRevision(revision);
            AuditMutation(document.AgencyId, actor, "DmsDocumentCreated", "Document", document.Id.ToString("D"), $"{document.Title} v1");
            return BuildDmsSummary(document);
        }
    }

    public IReadOnlyList<DmsDocumentRevisionDto> GetDmsRevisions(Guid documentId)
    {
        lock (_gate)
        {
            var document = RequireDocument(documentId);
            EnsureCurrentDocumentRevision(document);
            return _dmsRevisions.Values.Where(x => x.DocumentId == documentId).OrderByDescending(x => x.Version).ToArray();
        }
    }

    public DmsDocumentRevisionDto CreateDmsRevision(Guid documentId, CreateDmsRevisionRequest request, string actor)
    {
        lock (_gate)
        {
            var document = RequireDocument(documentId);
            EnsureCurrentDocumentRevision(document);
            var next = _dmsRevisions.Values.Where(x => x.DocumentId == documentId).Select(x => x.Version).DefaultIfEmpty(document.Version).Max() + 1;
            var content = request.Content ?? string.Empty;
            var now = DateTimeOffset.UtcNow;
            var revision = new DmsDocumentRevisionDto(Guid.NewGuid(), documentId, next, content, "Draft", actor, Sha256(content), now, null, null);
            _dmsRevisions[revision.Id] = revision;
            EnsureLegacyRevision(revision);
            _core.ApplyDmsDocumentRevision(documentId, next, content, "Draft", actor);
            AuditMutation(document.AgencyId, actor, "DmsRevisionCreated", "Document", documentId.ToString("D"), $"v{next} sha256={revision.ContentSha256}");
            return revision;
        }
    }

    public DmsDocumentRevisionDto PublishDmsRevision(Guid documentId, int version, PublishDmsRevisionRequest request, string actor)
    {
        lock (_gate)
        {
            var document = RequireDocument(documentId);
            EnsureCurrentDocumentRevision(document);
            var revision = _dmsRevisions.Values.SingleOrDefault(x => x.DocumentId == documentId && x.Version == version)
                ?? throw new KeyNotFoundException("Document revision not found.");
            if (revision.State == "Published") return revision;
            if (revision.State != "Draft") throw new InvalidOperationException("Only a draft revision can be published.");
            if (request.RequireApprovedWorkflow)
            {
                var approved = _approvalWorkflows.Values.Any(x => x.AgencyId == document.AgencyId && x.EntityType.Equals("DocumentVersion", StringComparison.OrdinalIgnoreCase) && x.EntityId == revision.Id && x.State == "Approved");
                if (!approved) throw new InvalidOperationException("An approved DocumentVersion workflow is required before publication.");
            }
            var now = DateTimeOffset.UtcNow;
            foreach (var prior in _dmsRevisions.Values.Where(x => x.DocumentId == documentId && x.State == "Published" && x.Id != revision.Id).ToArray())
            {
                _dmsRevisions[prior.Id] = prior with { State = "Superseded" };
                UpdateLegacyRevisionState(prior.DocumentId, prior.Version, "Superseded");
            }
            var published = revision with { State = "Published", PublishedBy = actor, PublishedAtUtc = now };
            _dmsRevisions[revision.Id] = published;
            UpdateLegacyRevisionState(documentId, version, "Published");
            _core.ApplyDmsDocumentRevision(documentId, version, revision.Content, "Published", actor);
            AuditMutation(document.AgencyId, actor, "DmsRevisionPublished", "Document", documentId.ToString("D"), $"v{version} sha256={revision.ContentSha256}");
            return published;
        }
    }

    public ApprovalWorkflowDto StartDmsRevisionApproval(Guid documentId, int version, DmsApprovalRequest request, string actor)
    {
        lock (_gate)
        {
            var document = RequireDocument(documentId);
            EnsureCurrentDocumentRevision(document);
            var revision = _dmsRevisions.Values.SingleOrDefault(x => x.DocumentId == documentId && x.Version == version)
                ?? throw new KeyNotFoundException("Document revision not found.");
            if (revision.State != "Draft") throw new InvalidOperationException("Only a draft revision can enter approval.");
            if (_approvalWorkflows.Values.Any(x => x.EntityType.Equals("DocumentVersion", StringComparison.OrdinalIgnoreCase) && x.EntityId == revision.Id && x.State == "Active"))
                throw new InvalidOperationException("An active approval workflow already exists for this revision.");
            return CreateApprovalWorkflow(new CreateApprovalWorkflowRequest(document.AgencyId, "DocumentVersion", revision.Id, request.Name, request.Mode, request.Steps), actor);
        }
    }

    public DmsDistributionDto CreateDmsDistribution(Guid documentId, CreateDmsDistributionRequest request, string actor)
    {
        lock (_gate)
        {
            var document = RequireDocument(documentId);
            EnsureCurrentDocumentRevision(document);
            var revision = _dmsRevisions.Values.SingleOrDefault(x => x.DocumentId == documentId && x.Version == request.DocumentVersion)
                ?? throw new KeyNotFoundException("Document revision not found.");
            if (revision.State != "Published") throw new InvalidOperationException("Only a published document revision can be distributed.");
            var scopeType = NormalizeScopeType(request.ScopeType);
            var scopeValue = request.ScopeValue?.Trim() ?? string.Empty;
            if (scopeType != "Agency" && string.IsNullOrWhiteSpace(scopeValue)) throw new ArgumentException($"{scopeType} scope requires a scope value.");
            var recipients = request.Recipients.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (scopeType == "User" && recipients.Count == 0) recipients.Add(scopeValue);
            if (request.RequiredRead && recipients.Count == 0)
                throw new InvalidOperationException("Required-read distribution needs explicit recipients until directory/group expansion is configured.");
            var now = DateTimeOffset.UtcNow;
            var distribution = new DmsDistributionDto(Guid.NewGuid(), document.AgencyId, documentId, revision.Version, scopeType, scopeValue, request.RequiredRead, request.DueAtUtc, "Active", actor, now);
            _dmsDistributions[distribution.Id] = distribution;
            foreach (var recipient in recipients)
            {
                var receipt = new DmsReadReceiptDto(Guid.NewGuid(), distribution.Id, documentId, revision.Version, recipient, "Assigned", now, null, null, null);
                _dmsReadReceipts[receipt.Id] = receipt;
            }
            AuditMutation(document.AgencyId, actor, "DmsDistributed", "Document", documentId.ToString("D"), $"v{revision.Version} {scopeType}:{scopeValue} requiredRead={request.RequiredRead} recipients={recipients.Count}");
            return distribution;
        }
    }

    public IReadOnlyList<DmsDistributionDto> GetDmsDistributions(Guid documentId)
    {
        lock (_gate)
        {
            RequireDocument(documentId);
            return _dmsDistributions.Values.Where(x => x.DocumentId == documentId).OrderByDescending(x => x.AssignedAtUtc).ToArray();
        }
    }

    public IReadOnlyList<DmsReadReceiptDto> GetDmsReadReceipts(Guid documentId)
    {
        lock (_gate)
        {
            RequireDocument(documentId);
            return _dmsReadReceipts.Values.Where(x => x.DocumentId == documentId).OrderBy(x => x.Recipient, StringComparer.OrdinalIgnoreCase).ThenByDescending(x => x.DocumentVersion).ToArray();
        }
    }

    public DmsRequiredReadStatusDto GetDmsRequiredReadStatus(Guid documentId, int version)
    {
        lock (_gate)
        {
            RequireDocument(documentId);
            var now = DateTimeOffset.UtcNow;
            var receipts = _dmsReadReceipts.Values.Where(x => x.DocumentId == documentId && x.DocumentVersion == version).OrderBy(x => x.Recipient, StringComparer.OrdinalIgnoreCase).ToArray();
            var distributionIds = receipts.Select(x => x.DistributionId).ToHashSet();
            var overdue = receipts.Count(x => x.State != "Acknowledged" && _dmsDistributions.TryGetValue(x.DistributionId, out var d) && d.RequiredRead && d.DueAtUtc is DateTimeOffset due && due < now);
            return new DmsRequiredReadStatusDto(documentId, version, receipts.Length, receipts.Count(x => x.ReadAtUtc is not null), receipts.Count(x => x.AcknowledgedAtUtc is not null), receipts.Count(x => x.AcknowledgedAtUtc is null), overdue, receipts);
        }
    }

    public DmsReadReceiptDto MarkDmsRead(Guid receiptId, string actor)
    {
        lock (_gate)
        {
            if (!_dmsReadReceipts.TryGetValue(receiptId, out var current)) throw new KeyNotFoundException("Read receipt not found.");
            RequireReceiptActor(current, actor);
            if (current.ReadAtUtc is not null) return current;
            var updated = current with { State = "Read", ReadAtUtc = DateTimeOffset.UtcNow };
            _dmsReadReceipts[receiptId] = updated;
            var document = RequireDocument(current.DocumentId);
            AuditMutation(document.AgencyId, actor, "DmsDocumentRead", "Document", current.DocumentId.ToString("D"), $"v{current.DocumentVersion}");
            return updated;
        }
    }

    public DmsReadReceiptDto AcknowledgeDmsRead(Guid receiptId, string actor)
    {
        lock (_gate)
        {
            if (!_dmsReadReceipts.TryGetValue(receiptId, out var current)) throw new KeyNotFoundException("Read receipt not found.");
            RequireReceiptActor(current, actor);
            if (current.AcknowledgedAtUtc is not null) return current;
            var now = DateTimeOffset.UtcNow;
            var updated = current with { State = "Acknowledged", ReadAtUtc = current.ReadAtUtc ?? now, AcknowledgedAtUtc = now, AcknowledgedBy = actor };
            _dmsReadReceipts[receiptId] = updated;
            var document = RequireDocument(current.DocumentId);
            AuditMutation(document.AgencyId, actor, "DmsRequiredReadAcknowledged", "Document", current.DocumentId.ToString("D"), $"v{current.DocumentVersion}");
            return updated;
        }
    }

    public DmsAssetReferenceDto AddDmsAssetReference(Guid documentId, AddDmsAssetReferenceRequest request, string actor)
    {
        lock (_gate)
        {
            var document = RequireDocument(documentId);
            EnsureCurrentDocumentRevision(document);
            var revision = _dmsRevisions.Values.SingleOrDefault(x => x.DocumentId == documentId && x.Version == request.DocumentVersion)
                ?? throw new KeyNotFoundException("Document revision not found.");
            if (revision.State != "Draft") throw new InvalidOperationException("Asset references can only be added while the document revision is Draft.");
            if (string.IsNullOrWhiteSpace(request.FileName)) throw new ArgumentException("Asset file name is required.");
            if (string.IsNullOrWhiteSpace(request.StorageReference)) throw new ArgumentException("Asset storage reference is required.");
            var sha = NormalizeOptionalSha256(request.ContentSha256);
            var item = new DmsAssetReferenceDto(Guid.NewGuid(), document.AgencyId, documentId, revision.Version, request.FileName.Trim(), string.IsNullOrWhiteSpace(request.ContentType) ? "application/octet-stream" : request.ContentType.Trim(), request.StorageReference.Trim(), sha, string.IsNullOrWhiteSpace(request.Classification) ? document.Classification : request.Classification.Trim(), actor, DateTimeOffset.UtcNow);
            _dmsAssetReferences[item.Id] = item;
            AuditMutation(document.AgencyId, actor, "DmsAssetReferenceAdded", "Document", documentId.ToString("D"), $"v{revision.Version} {item.FileName} storage={item.StorageReference} sha256={item.ContentSha256 ?? "unverified"}");
            return item;
        }
    }

    public IReadOnlyList<DmsAssetReferenceDto> GetDmsAssetReferences(Guid documentId)
    {
        lock (_gate)
        {
            RequireDocument(documentId);
            return _dmsAssetReferences.Values.Where(x => x.DocumentId == documentId).OrderByDescending(x => x.AddedAtUtc).ToArray();
        }
    }

    public DmsDocumentLinkDto LinkDmsDocument(Guid documentId, CreateDmsDocumentLinkRequest request, string actor)
    {
        lock (_gate)
        {
            var document = RequireDocument(documentId);
            EnsureCurrentDocumentRevision(document);
            if (!_dmsRevisions.Values.Any(x => x.DocumentId == documentId && x.Version == request.DocumentVersion)) throw new KeyNotFoundException("Document revision not found.");
            var entityType = NormalizeEntityType(request.EntityType);
            var entityKey = request.EntityKey?.Trim() ?? string.Empty;
            ValidateLinkTarget(document.AgencyId, entityType, entityKey);
            var relationship = string.IsNullOrWhiteSpace(request.Relationship) ? "Reference" : request.Relationship.Trim();
            var existing = _dmsDocumentLinks.Values.FirstOrDefault(x => x.DocumentId == documentId && x.DocumentVersion == request.DocumentVersion && x.EntityType == entityType && x.EntityKey.Equals(entityKey, StringComparison.OrdinalIgnoreCase) && x.Relationship.Equals(relationship, StringComparison.OrdinalIgnoreCase));
            if (existing is not null) return existing;
            var link = new DmsDocumentLinkDto(Guid.NewGuid(), document.AgencyId, documentId, request.DocumentVersion, entityType, entityKey, relationship, actor, DateTimeOffset.UtcNow);
            _dmsDocumentLinks[link.Id] = link;
            AuditMutation(document.AgencyId, actor, "DmsDocumentLinked", "Document", documentId.ToString("D"), $"v{request.DocumentVersion} -> {entityType}:{entityKey} ({relationship})");
            return link;
        }
    }

    public IReadOnlyList<DmsDocumentLinkDto> GetDmsDocumentLinks(Guid documentId)
    {
        lock (_gate)
        {
            RequireDocument(documentId);
            return _dmsDocumentLinks.Values.Where(x => x.DocumentId == documentId).OrderByDescending(x => x.LinkedAtUtc).ToArray();
        }
    }

    public IReadOnlyList<DmsDocumentLinkDto> GetDmsEntityLinks(string entityType, string entityKey)
    {
        lock (_gate)
        {
            var normalizedType = NormalizeEntityType(entityType);
            return _dmsDocumentLinks.Values.Where(x => x.EntityType == normalizedType && x.EntityKey.Equals(entityKey.Trim(), StringComparison.OrdinalIgnoreCase)).OrderByDescending(x => x.LinkedAtUtc).ToArray();
        }
    }

    public FormSignatureDto SignFormStrong(Guid formId, SignFormStrongRequest request, string actor, string source)
    {
        lock (_gate)
        {
            if (!_forms.TryGetValue(formId, out var form)) throw new KeyNotFoundException("Form not found.");
            if (form.Status == "Signed")
            {
                var existing = _formSignatures.Values.SingleOrDefault(x => x.FormId == formId);
                return existing ?? throw new InvalidOperationException("Signed form is missing its immutable signature record.");
            }
            if (form.Status != "Draft") throw new InvalidOperationException("Only a draft form can be signed.");
            if (!_formTemplates.TryGetValue(form.TemplateId, out var template)) throw new KeyNotFoundException("Form template not found.");
            if (template.AgencyId != form.AgencyId) throw new InvalidOperationException("Form and template agency do not match.");
            ValidateJson(form.ValuesJson, "Form values");
            var now = DateTimeOffset.UtcNow;
            var signature = new FormSignatureDto(Guid.NewGuid(), form.Id, template.Id, template.Version, actor, string.IsNullOrWhiteSpace(request.Meaning) ? "Approved / certified by signer" : request.Meaning.Trim(), Sha256(form.ValuesJson), source, now);
            _formSignatures[signature.Id] = signature;
            _forms[form.Id] = form with { Status = "Signed", SignedBy = actor, UpdatedAtUtc = now };
            AuditMutation(form.AgencyId, actor, "FormSigned", "Form", form.Id.ToString("D"), $"template={template.Name} v{template.Version} valuesSha256={signature.ValuesSha256}");
            return signature;
        }
    }

    public IReadOnlyList<FormSignatureDto> GetFormSignatures(Guid formId)
    {
        lock (_gate) return _formSignatures.Values.Where(x => x.FormId == formId).OrderBy(x => x.SignedAtUtc).ToArray();
    }

    private DmsDocumentSummaryDto BuildDmsSummary(DocumentDto document)
    {
        EnsureCurrentDocumentRevision(document);
        var revisions = _dmsRevisions.Values.Where(x => x.DocumentId == document.Id).ToArray();
        var publishedVersion = revisions.Where(x => x.State == "Published").Select(x => x.Version).DefaultIfEmpty(0).Max();
        var activeDistributions = _dmsDistributions.Values.Count(x => x.DocumentId == document.Id && x.State == "Active");
        var receipts = _dmsReadReceipts.Values.Where(x => x.DocumentId == document.Id).ToArray();
        var now = DateTimeOffset.UtcNow;
        var overdue = receipts.Count(x => x.AcknowledgedAtUtc is null && _dmsDistributions.TryGetValue(x.DistributionId, out var distribution) && distribution.RequiredRead && distribution.DueAtUtc is DateTimeOffset due && due < now);
        return new DmsDocumentSummaryDto(document.Id, document.AgencyId, document.Title, document.Classification, document.Version, document.Status, document.CreatedBy, document.CreatedAtUtc, document.UpdatedAtUtc, publishedVersion, activeDistributions, receipts.Length, receipts.Count(x => x.AcknowledgedAtUtc is not null), overdue);
    }

    private DocumentDto RequireDocument(Guid documentId) => _core.Documents.FirstOrDefault(x => x.Id == documentId) ?? throw new KeyNotFoundException("Document not found.");

    private void EnsureCurrentDocumentRevision(DocumentDto document)
    {
        if (_dmsRevisions.Values.Any(x => x.DocumentId == document.Id && x.Version == document.Version)) return;
        var legacy = _documentVersions.TryGetValue(document.Id, out var versions) ? versions.FirstOrDefault(x => x.Version == document.Version) : null;
        var content = legacy?.Content ?? document.Content;
        var state = legacy?.Status ?? document.Status;
        var author = legacy?.Author ?? document.CreatedBy;
        var created = legacy?.CreatedAtUtc ?? document.CreatedAtUtc;
        var revision = new DmsDocumentRevisionDto(legacy?.Id ?? Guid.NewGuid(), document.Id, document.Version, content, NormalizeRevisionState(state), author, Sha256(content), created, state.Equals("Published", StringComparison.OrdinalIgnoreCase) ? document.CreatedBy : null, state.Equals("Published", StringComparison.OrdinalIgnoreCase) ? document.UpdatedAtUtc : null);
        _dmsRevisions[revision.Id] = revision;
        EnsureLegacyRevision(revision);
    }

    private void EnsureLegacyRevision(DmsDocumentRevisionDto revision)
    {
        if (!_documentVersions.TryGetValue(revision.DocumentId, out var list)) _documentVersions[revision.DocumentId] = list = [];
        var existingIndex = list.FindIndex(x => x.Version == revision.Version);
        var legacy = new DocumentVersionDto(revision.Id, revision.DocumentId, revision.Version, revision.Content, revision.State, revision.Author, revision.CreatedAtUtc);
        if (existingIndex >= 0) list[existingIndex] = legacy; else list.Add(legacy);
    }

    private void UpdateLegacyRevisionState(Guid documentId, int version, string state)
    {
        if (!_documentVersions.TryGetValue(documentId, out var list)) return;
        var index = list.FindIndex(x => x.Version == version);
        if (index >= 0) list[index] = list[index] with { Status = state };
    }

    private static string NormalizeRevisionState(string state) => state.Trim().ToLowerInvariant() switch
    {
        "published" => "Published",
        "superseded" => "Superseded",
        _ => "Draft"
    };

    private static string NormalizeScopeType(string scopeType) => scopeType.Trim().ToLowerInvariant() switch
    {
        "agency" => "Agency",
        "station" => "Station",
        "role" => "Role",
        "user" or "person" or "employee" => "User",
        _ => throw new ArgumentException("Distribution scope must be Agency, Station, Role, or User.")
    };

    private static string NormalizeEntityType(string entityType) => entityType.Trim().ToLowerInvariant() switch
    {
        "incident" => "Incident",
        "rms" or "rmsreport" or "rmsreporting" => "RmsReport",
        "unit" or "apparatus" => "Unit",
        "station" => "Station",
        "form" => "Form",
        "person" or "personnel" or "employee" => "Personnel",
        _ => throw new ArgumentException("Entity type must be Incident, RmsReport, Unit, Station, Form, or Personnel.")
    };

    private void ValidateApprovalTargetAgency(Guid agencyId, string entityType, Guid entityId)
    {
        switch (entityType.Trim().ToLowerInvariant())
        {
            case "documentversion":
                if (!_dmsRevisions.TryGetValue(entityId, out var revision)) throw new KeyNotFoundException("Document revision approval target not found.");
                if (RequireDocument(revision.DocumentId).AgencyId != agencyId) throw new InvalidOperationException("Approval workflow and document revision agency do not match.");
                break;
            case "document":
                if (RequireDocument(entityId).AgencyId != agencyId) throw new InvalidOperationException("Approval workflow and document agency do not match.");
                break;
            case "formtemplate":
                if (!_formTemplates.TryGetValue(entityId, out var template)) throw new KeyNotFoundException("Form template approval target not found.");
                if (template.AgencyId != agencyId) throw new InvalidOperationException("Approval workflow and form template agency do not match.");
                break;
            case "form":
                if (!_forms.TryGetValue(entityId, out var form)) throw new KeyNotFoundException("Form approval target not found.");
                if (form.AgencyId != agencyId) throw new InvalidOperationException("Approval workflow and form agency do not match.");
                break;
            case "rmsreport":
                var report = _core.RmsIncidentReports.FirstOrDefault(x => x.Id == entityId) ?? throw new KeyNotFoundException("RMS report approval target not found.");
                if (report.AgencyId != agencyId) throw new InvalidOperationException("Approval workflow and RMS report agency do not match.");
                break;
        }
    }

    private void ValidateLinkTarget(Guid agencyId, string entityType, string entityKey)
    {
        if (string.IsNullOrWhiteSpace(entityKey)) throw new ArgumentException("Entity key is required.");
        if (entityType == "Personnel") return;
        if (!Guid.TryParse(entityKey, out var id)) throw new ArgumentException($"{entityType} entity key must be a GUID.");
        switch (entityType)
        {
            case "Incident":
                if (_core.GetIncident(id).AgencyId != agencyId) throw new InvalidOperationException("Document and incident agency do not match.");
                break;
            case "RmsReport":
                var report = _core.RmsIncidentReports.FirstOrDefault(x => x.Id == id) ?? throw new KeyNotFoundException("RMS report not found.");
                if (report.AgencyId != agencyId) throw new InvalidOperationException("Document and RMS report agency do not match.");
                break;
            case "Unit":
                var unit = _core.Units.FirstOrDefault(x => x.Id == id) ?? throw new KeyNotFoundException("Unit not found.");
                if (unit.AgencyId != agencyId) throw new InvalidOperationException("Document and unit agency do not match.");
                break;
            case "Station":
                var station = _core.Stations.FirstOrDefault(x => x.Id == id) ?? throw new KeyNotFoundException("Station not found.");
                if (station.AgencyId != agencyId) throw new InvalidOperationException("Document and station agency do not match.");
                break;
            case "Form":
                if (!_forms.TryGetValue(id, out var form)) throw new KeyNotFoundException("Form not found.");
                if (form.AgencyId != agencyId) throw new InvalidOperationException("Document and form agency do not match.");
                break;
        }
    }

    private static void ValidateJson(string json, string label)
    {
        try { using var _ = JsonDocument.Parse(json); }
        catch (JsonException ex) { throw new ArgumentException($"{label} must be valid JSON.", ex); }
    }

    private static string? NormalizeOptionalSha256(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length != 64 || normalized.Any(c => !Uri.IsHexDigit(c))) throw new ArgumentException("Asset SHA-256 must be 64 hexadecimal characters when supplied.");
        return normalized;
    }

    private static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty))).ToLowerInvariant();

    private static void RequireReceiptActor(DmsReadReceiptDto receipt, string actor)
    {
        if (actor.Equals("local-development", StringComparison.OrdinalIgnoreCase)) return;
        if (!receipt.Recipient.Equals(actor, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Required-read receipt belongs to another user.");
    }
}

public sealed partial class OperationalStore
{
    internal void ApplyDmsDocumentRevision(Guid documentId, int version, string content, string status, string actor)
    {
        lock (_gate)
        {
            if (!_documents.TryGetValue(documentId, out var document)) throw new KeyNotFoundException("Document not found.");
            var now = DateTimeOffset.UtcNow;
            _documents[documentId] = document with { Version = version, Content = content, Status = status, UpdatedAtUtc = now };
            AddReplay(document.AgencyId, null, "Documents", "DocumentRevisionStateChanged", $"{document.Title} v{version} {status}", actor);
        }
    }
}
