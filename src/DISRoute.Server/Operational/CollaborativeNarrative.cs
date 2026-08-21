using DISRoute.Contracts.Operational;

namespace DISRoute.Server.Operational;

public sealed partial class FeatureStore
{
    public CollaborativeNarrativeDto GetCollaborativeNarrative(Guid incidentId)
    {
        lock (_gate)
        {
            _core.GetIncident(incidentId);
            if (_collaborativeNarratives.TryGetValue(incidentId, out var current)) return current;
            current = new CollaborativeNarrativeDto(incidentId, 0, string.Empty, "system", DateTimeOffset.UtcNow);
            _collaborativeNarratives[incidentId] = current;
            return current;
        }
    }

    public IReadOnlyList<CollaborativeNarrativeOperationDto> GetCollaborativeNarrativeOperations(Guid incidentId, long afterVersion = -1)
    {
        lock (_gate)
        {
            _core.GetIncident(incidentId);
            if (!_collaborativeNarrativeOperations.TryGetValue(incidentId, out var operations)) return [];
            return operations.Where(x => x.ResultVersion > afterVersion).OrderBy(x => x.ResultVersion).ToArray();
        }
    }

    public CollaborativeNarrativeDto PatchCollaborativeNarrative(Guid incidentId, CollaborativeNarrativePatchRequest request)
    {
        lock (_gate)
        {
            if (TryIdempotent<CollaborativeNarrativeDto>(request.IdempotencyKey, new { TargetId = incidentId, Request = request }, out var remembered)) return remembered;
            var current = GetCollaborativeNarrative(incidentId);
            if (request.ExpectedVersion < 0 || request.ExpectedVersion > current.Version)
                throw new InvalidOperationException($"Concurrency conflict: expected narrative version {request.ExpectedVersion}, current version is {current.Version}.");

            var start = request.Start;
            var deleteCount = request.DeleteCount;
            var rebased = false;
            if (request.ExpectedVersion < current.Version)
            {
                (start, deleteCount) = RebasePatch(incidentId, request.ExpectedVersion, start, deleteCount);
                rebased = true;
            }

            ValidatePatch(current.Text, start, deleteCount);
            var insertText = request.InsertText ?? string.Empty;
            var appliedBaseVersion = current.Version;
            var updatedText = current.Text.Remove(start, deleteCount).Insert(start, insertText);
            var updatedAt = DateTimeOffset.UtcNow;
            var updated = current with { Version = current.Version + 1, Text = updatedText, LastEditor = request.Actor, UpdatedAtUtc = updatedAt };
            _collaborativeNarratives[incidentId] = updated;

            var operation = new CollaborativeNarrativeOperationDto(
                Guid.NewGuid(), incidentId, request.ExpectedVersion, appliedBaseVersion, updated.Version,
                start, deleteCount, insertText, request.Actor, rebased, updatedAt);
            if (!_collaborativeNarrativeOperations.TryGetValue(incidentId, out var operations))
                _collaborativeNarrativeOperations[incidentId] = operations = [];
            operations.Add(operation);
            TrimOperationHistory(operations);

            AuditMutation(_core.GetIncident(incidentId).AgencyId, request.Actor, rebased ? "NarrativePatchRebased" : "NarrativePatched", "Incident", incidentId.ToString("D"),
                $"Collaborative narrative version {updated.Version} (client base {request.ExpectedVersion}, applied base {appliedBaseVersion})");
            Remember(request.IdempotencyKey, new { TargetId = incidentId, Request = request }, updated);
            return updated;
        }
    }

    public CollaborativeNarrativeDto ReplaceCollaborativeNarrative(Guid incidentId, ReplaceCollaborativeNarrativeRequest request)
    {
        lock (_gate)
        {
            if (TryIdempotent<CollaborativeNarrativeDto>(request.IdempotencyKey, new { TargetId = incidentId, Request = request }, out var remembered)) return remembered;
            var current = GetCollaborativeNarrative(incidentId);
            if (current.Version != request.ExpectedVersion)
                throw new InvalidOperationException($"Concurrency conflict: expected narrative version {request.ExpectedVersion}, current version is {current.Version}.");
            var text = request.Text ?? string.Empty;
            var updatedAt = DateTimeOffset.UtcNow;
            var updated = current with { Version = current.Version + 1, Text = text, LastEditor = request.Actor, UpdatedAtUtc = updatedAt };
            _collaborativeNarratives[incidentId] = updated;
            if (!_collaborativeNarrativeOperations.TryGetValue(incidentId, out var operations))
                _collaborativeNarrativeOperations[incidentId] = operations = [];
            operations.Add(new CollaborativeNarrativeOperationDto(
                Guid.NewGuid(), incidentId, current.Version, current.Version, updated.Version,
                0, current.Text.Length, text, request.Actor, false, updatedAt));
            TrimOperationHistory(operations);
            AuditMutation(_core.GetIncident(incidentId).AgencyId, request.Actor, "NarrativeReplaced", "Incident", incidentId.ToString("D"), $"Collaborative narrative version {updated.Version}");
            Remember(request.IdempotencyKey, new { TargetId = incidentId, Request = request }, updated);
            return updated;
        }
    }

    private (int Start, int DeleteCount) RebasePatch(Guid incidentId, long clientBaseVersion, int start, int deleteCount)
    {
        if (!_collaborativeNarrativeOperations.TryGetValue(incidentId, out var history))
            throw new InvalidOperationException($"Concurrency conflict: narrative operation history is unavailable for client version {clientBaseVersion}.");

        var required = history.Where(x => x.ResultVersion > clientBaseVersion).OrderBy(x => x.ResultVersion).ToArray();
        if (required.Length == 0 || required[0].AppliedBaseVersion > clientBaseVersion)
            throw new InvalidOperationException($"Concurrency conflict: narrative operation history no longer covers client version {clientBaseVersion}.");

        var transformedStart = start;
        var transformedEnd = checked(start + deleteCount);
        foreach (var prior in required)
        {
            var priorStart = prior.Start;
            var priorEnd = checked(prior.Start + prior.DeleteCount);
            var delta = prior.InsertText.Length - prior.DeleteCount;
            var incomingInsertOnly = transformedStart == transformedEnd;
            var priorInsertOnly = priorStart == priorEnd;

            // A prior operation wholly before this edit shifts the edit by its net length change.
            // Concurrent inserts at the same position are ordered by server commit order, so the
            // later incoming insert is placed after the already-committed text.
            var priorBefore = priorEnd < transformedStart
                || (priorEnd == transformedStart && (priorStart < transformedStart || priorInsertOnly));
            if (priorBefore)
            {
                transformedStart = checked(transformedStart + delta);
                transformedEnd = checked(transformedEnd + delta);
                continue;
            }

            // A prior operation wholly after this edit has no effect on its coordinates.
            if (transformedEnd <= priorStart) continue;

            // Overlapping concurrent replacements/deletions are deliberately not guessed at.
            // Returning a conflict is safer for operational narrative than silently deleting or
            // duplicating another dispatcher's text. Non-overlapping edits rebase automatically.
            if (!incomingInsertOnly || !priorInsertOnly)
                throw new InvalidOperationException(
                    $"Concurrency conflict: narrative edit based on version {clientBaseVersion} overlaps committed version {prior.ResultVersion}. Refresh before retrying the overlapping edit.");
        }

        return (transformedStart, checked(transformedEnd - transformedStart));
    }

    private static void ValidatePatch(string text, int start, int deleteCount)
    {
        if (start < 0 || start > text.Length)
            throw new ArgumentOutOfRangeException(nameof(start), start, "Patch start is outside the current narrative.");
        if (deleteCount < 0 || start + deleteCount > text.Length)
            throw new ArgumentOutOfRangeException(nameof(deleteCount), deleteCount, "Patch delete count is outside the current narrative.");
    }

    private static void TrimOperationHistory(List<CollaborativeNarrativeOperationDto> operations)
    {
        const int maxOperationsPerIncident = 4096;
        if (operations.Count > maxOperationsPerIncident)
            operations.RemoveRange(0, operations.Count - maxOperationsPerIncident);
    }
}
