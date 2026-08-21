using System.Security.Cryptography;
using System.Text;
using System.Globalization;
using DISRoute.Contracts.Operational;

namespace DISRoute.Server.Operational;

public sealed partial class FeatureStore
{
    private readonly Dictionary<Guid, StaffingRuleDto> _staffingRules = [];
    private readonly Dictionary<Guid, WorkforceAssignmentDto> _workforceAssignments = [];
    private readonly Dictionary<Guid, WorkforceRosterSnapshotDto> _workforceRosterSnapshots = [];
    private readonly List<WorkforceScheduleChangeDto> _workforceScheduleChanges = [];

    public IReadOnlyList<StaffingRuleDto> StaffingRules { get { lock (_gate) return _staffingRules.Values.OrderBy(x => x.ShiftName).ThenBy(x => x.Position).ToArray(); } }
    public IReadOnlyList<WorkforceAssignmentDto> WorkforceAssignments { get { lock (_gate) return _workforceAssignments.Values.OrderBy(x => x.Date).ThenBy(x => x.ShiftName).ThenBy(x => x.Position).ToArray(); } }
    public IReadOnlyList<WorkforceRosterSnapshotDto> WorkforceRosterSnapshots { get { lock (_gate) return _workforceRosterSnapshots.Values.OrderByDescending(x => x.Date).ThenByDescending(x => x.Revision).ToArray(); } }
    public IReadOnlyList<WorkforceScheduleChangeDto> WorkforceScheduleChanges { get { lock (_gate) return _workforceScheduleChanges.OrderByDescending(x => x.OccurredAtUtc).ToArray(); } }

    public StaffingRuleDto UpsertStaffingRule(Guid? id, UpsertStaffingRuleRequest request, string actor)
    {
        lock (_gate)
        {
            if (request.MinimumCount < 1) throw new ArgumentException("Minimum staffing count must be at least 1.");
            RequireText(request.ShiftName, "Shift name");
            RequireText(request.Position, "Position");
            if (request.StationId is Guid stationId)
            {
                var station = _core.Stations.FirstOrDefault(x => x.Id == stationId) ?? throw new KeyNotFoundException("Station not found.");
                if (station.AgencyId != request.AgencyId) throw new InvalidOperationException("Staffing rule station belongs to another agency.");
            }
            var key = id ?? Guid.NewGuid();
            if (id is not null && !_staffingRules.ContainsKey(key)) throw new KeyNotFoundException("Staffing rule not found.");
            var item = new StaffingRuleDto(key, request.AgencyId, request.StationId, request.ShiftName.Trim(), request.Position.Trim(), request.MinimumCount, request.RequiredQualification.Trim(), request.Enabled, actor, DateTimeOffset.UtcNow);
            _staffingRules[key] = item;
            RecordWorkforceChange(item.AgencyId, DateOnly.FromDateTime(DateTime.UtcNow), null, id is null ? "StaffingRuleCreated" : "StaffingRuleUpdated", $"{item.ShiftName} {item.Position} minimum={item.MinimumCount} qualification={item.RequiredQualification}", actor);
            AuditMutation(item.AgencyId, actor, id is null ? "StaffingRuleCreated" : "StaffingRuleUpdated", "StaffingRule", key.ToString("D"), $"{item.ShiftName} {item.Position} minimum={item.MinimumCount}");
            return item;
        }
    }

    public WorkforceAssignmentDto UpsertWorkforceAssignment(Guid? id, UpsertWorkforceAssignmentRequest request, string actor)
    {
        lock (_gate)
        {
            RequireText(request.ShiftName, "Shift name");
            RequireText(request.Position, "Position");
            var station = _core.Stations.FirstOrDefault(x => x.Id == request.StationId) ?? throw new KeyNotFoundException("Station not found.");
            if (station.AgencyId != request.AgencyId) throw new InvalidOperationException("Assignment station belongs to another agency.");
            if (request.UnitId is Guid unitId)
            {
                var unit = _core.Units.FirstOrDefault(x => x.Id == unitId) ?? throw new KeyNotFoundException("Unit not found.");
                if (unit.AgencyId != request.AgencyId || unit.StationId != request.StationId) throw new InvalidOperationException("Assignment unit must belong to the selected agency and station.");
            }
            var state = NormalizeAssignmentState(request.State);
            if (state != "Open" && string.IsNullOrWhiteSpace(request.EmployeeName)) throw new ArgumentException("Employee name is required unless the assignment is Open.");
            if (request.EndsAtUtc is DateTimeOffset ends && request.EffectiveAtUtc is DateTimeOffset starts && ends < starts) throw new ArgumentException("Assignment end cannot precede its effective time.");
            var key = id ?? Guid.NewGuid();
            if (id is not null && !_workforceAssignments.ContainsKey(key)) throw new KeyNotFoundException("Workforce assignment not found.");
            var now = DateTimeOffset.UtcNow;
            var item = new WorkforceAssignmentDto(key, request.AgencyId, request.Date, request.ShiftName.Trim(), request.StationId, request.UnitId, request.Position.Trim(), NullIfBlank(request.EmployeeId), request.EmployeeName.Trim(), request.Qualification.Trim(), NormalizeAssignmentType(request.AssignmentType), state, request.EffectiveAtUtc ?? now, request.EndsAtUtc, string.IsNullOrWhiteSpace(request.Source) ? "Manual" : request.Source.Trim(), actor, now);
            _workforceAssignments[key] = item;
            RecordWorkforceChange(item.AgencyId, item.Date, item.Id, id is null ? "AssignmentCreated" : "AssignmentUpdated", AssignmentSummary(item), actor);
            AuditMutation(item.AgencyId, actor, id is null ? "WorkforceAssignmentCreated" : "WorkforceAssignmentUpdated", "WorkforceAssignment", key.ToString("D"), AssignmentSummary(item));
            return item;
        }
    }

    public WorkforceAssignmentDto CallOutWorkforceAssignment(Guid id, WorkforceActionRequest request, string actor)
    {
        lock (_gate)
        {
            if (!_workforceAssignments.TryGetValue(id, out var current)) throw new KeyNotFoundException("Workforce assignment not found.");
            if (!IsFilledState(current.State)) throw new InvalidOperationException($"Only a filled assignment can be called out. Current state is '{current.State}'.");
            var now = DateTimeOffset.UtcNow;
            var updated = current with { State = "CallOut", EndsAtUtc = now, UpdatedBy = actor, UpdatedAtUtc = now };
            _workforceAssignments[id] = updated;
            var vacancy = new WorkforceAssignmentDto(Guid.NewGuid(), current.AgencyId, current.Date, current.ShiftName, current.StationId, current.UnitId, current.Position, null, string.Empty, current.Qualification, "Coverage", "Open", now, current.EndsAtUtc, "CallOutVacancy", actor, now);
            _workforceAssignments[vacancy.Id] = vacancy;
            var notes = string.IsNullOrWhiteSpace(request.Notes) ? string.Empty : $" — {request.Notes.Trim()}";
            RecordWorkforceChange(current.AgencyId, current.Date, current.Id, "CallOut", $"{current.EmployeeName} called out from {current.Position}; vacancy {vacancy.Id:D}{notes}", actor);
            AuditMutation(current.AgencyId, actor, "WorkforceCallOut", "WorkforceAssignment", current.Id.ToString("D"), $"{current.EmployeeName} {current.Position}{notes}");
            return updated;
        }
    }

    public WorkforceAssignmentDto RelieveWorkforceAssignment(Guid id, WorkforceReliefRequest request, string actor)
    {
        lock (_gate)
        {
            if (!_workforceAssignments.TryGetValue(id, out var current)) throw new KeyNotFoundException("Workforce assignment not found.");
            if (!IsFilledState(current.State)) throw new InvalidOperationException($"Only a filled assignment can be relieved. Current state is '{current.State}'.");
            RequireText(request.EmployeeName, "Relief employee name");
            var now = DateTimeOffset.UtcNow;
            _workforceAssignments[id] = current with { State = "Relieved", EndsAtUtc = now, UpdatedBy = actor, UpdatedAtUtc = now };
            var replacement = new WorkforceAssignmentDto(Guid.NewGuid(), current.AgencyId, current.Date, current.ShiftName, current.StationId, current.UnitId, current.Position, NullIfBlank(request.EmployeeId), request.EmployeeName.Trim(), request.Qualification.Trim(), NormalizeAssignmentType(request.AssignmentType), "Assigned", now, current.EndsAtUtc, "Relief", actor, now);
            _workforceAssignments[replacement.Id] = replacement;
            var notes = string.IsNullOrWhiteSpace(request.Notes) ? string.Empty : $" — {request.Notes.Trim()}";
            RecordWorkforceChange(current.AgencyId, current.Date, replacement.Id, "Relief", $"{current.EmployeeName} relieved by {replacement.EmployeeName} in {current.Position}{notes}", actor);
            AuditMutation(current.AgencyId, actor, "WorkforceRelief", "WorkforceAssignment", replacement.Id.ToString("D"), $"{current.EmployeeName} -> {replacement.EmployeeName} {current.Position}");
            return replacement;
        }
    }

    public WorkforceStaffingEvaluationDto EvaluateStaffing(Guid agencyId, DateOnly date, DateTimeOffset? asOfUtc = null)
    {
        lock (_gate)
        {
            var asOf = asOfUtc ?? DateTimeOffset.UtcNow;
            var rules = _staffingRules.Values.Where(x => x.AgencyId == agencyId && x.Enabled).ToArray();
            var assignments = CurrentAssignments(agencyId, date, asOf);
            var results = new List<StaffingRuleResultDto>();
            var warnings = new List<string>();
            foreach (var rule in rules)
            {
                var candidates = assignments.Where(x => IsFilledState(x.State)
                    && x.ShiftName.Equals(rule.ShiftName, StringComparison.OrdinalIgnoreCase)
                    && x.Position.Equals(rule.Position, StringComparison.OrdinalIgnoreCase)
                    && (rule.StationId is null || x.StationId == rule.StationId)
                    && HasQualification(x, rule.RequiredQualification, date)).ToArray();
                var vacancies = Math.Max(0, rule.MinimumCount - candidates.Length);
                var scope = rule.StationId is Guid stationId ? _core.Stations.FirstOrDefault(x => x.Id == stationId)?.Code ?? stationId.ToString("D") : "Agency";
                results.Add(new StaffingRuleResultDto(rule.Id, scope, rule.ShiftName, rule.Position, rule.RequiredQualification, rule.MinimumCount, candidates.Length, vacancies, candidates.Select(x => x.EmployeeName).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray()));
                if (vacancies > 0) warnings.Add($"{scope} {rule.ShiftName} {rule.Position}: {vacancies} vacancy/vacancies; qualification={rule.RequiredQualification}");
            }
            var required = results.Sum(x => x.RequiredCount);
            var filled = results.Sum(x => Math.Min(x.FilledCount, x.RequiredCount));
            if (rules.Length == 0)
            {
                var legacy = _core.Workforce.Where(x => x.AgencyId == agencyId && x.Date == date).ToArray();
                required = legacy.Length;
                filled = legacy.Count(x => !string.IsNullOrWhiteSpace(x.EmployeeName) && !x.Status.Equals("Open", StringComparison.OrdinalIgnoreCase));
                if (required == 0) warnings.Add("No staffing rules are configured for this agency/date.");
            }
            return new WorkforceStaffingEvaluationDto(agencyId, date, asOf, required, filled, Math.Max(0, required - filled), required == 0 || filled >= required, results, warnings);
        }
    }

    public WorkforceRosterSnapshotDto PublishWorkforceRoster(Guid agencyId, DateOnly date, PublishWorkforceRosterRequest request, string actor)
    {
        lock (_gate)
        {
            var assignments = CurrentAssignments(agencyId, date, DateTimeOffset.UtcNow);
            var staffing = EvaluateStaffing(agencyId, date);
            var revision = _workforceRosterSnapshots.Values.Where(x => x.AgencyId == agencyId && x.Date == date).Select(x => x.Revision).DefaultIfEmpty(0).Max() + 1;
            var now = DateTimeOffset.UtcNow;
            var notes = request.Notes?.Trim() ?? string.Empty;
            var hash = ComputeRosterHash(agencyId, date, revision, assignments, staffing);
            var item = new WorkforceRosterSnapshotDto(Guid.NewGuid(), agencyId, date, revision, staffing.MeetsMinimums ? "Posted" : "PostedWithDeficiencies", now, actor, notes, hash, assignments, staffing);
            _workforceRosterSnapshots[item.Id] = item;
            RecordWorkforceChange(agencyId, date, null, "RosterPosted", $"Roster revision {revision} posted state={item.State} hash={hash}", actor);
            AuditMutation(agencyId, actor, "WorkforceRosterPosted", "WorkforceRoster", item.Id.ToString("D"), $"{date:yyyy-MM-dd} revision={revision} state={item.State}");
            return item;
        }
    }

    public WorkforceRosterViewDto GetWorkforceRoster(Guid agencyId, DateOnly date, DateTimeOffset? asOfUtc = null)
    {
        lock (_gate)
        {
            var asOf = asOfUtc ?? DateTimeOffset.UtcNow;
            var assignments = CurrentAssignments(agencyId, date, asOf);
            var posted = _workforceRosterSnapshots.Values.Where(x => x.AgencyId == agencyId && x.Date == date && x.PostedAtUtc <= asOf).OrderByDescending(x => x.Revision).FirstOrDefault();
            var staffing = EvaluateStaffing(agencyId, date, asOf);
            var changes = _workforceScheduleChanges.Where(x => x.AgencyId == agencyId && x.Date == date && x.OccurredAtUtc <= asOf).OrderBy(x => x.OccurredAtUtc).ToArray();
            return new WorkforceRosterViewDto(agencyId, date, asOf, assignments, posted, staffing, changes);
        }
    }

    public WorkforceRosterPrintDto BuildWorkforceRosterPrint(Guid agencyId, DateOnly date, bool includeChanges)
    {
        lock (_gate)
        {
            var roster = GetWorkforceRoster(agencyId, date);
            var stationNames = _core.Stations.ToDictionary(x => x.Id, x => x.Code);
            var unitNames = _core.Units.ToDictionary(x => x.Id, x => x.CallSign);
            var lines = new List<string>
            {
                $"DISRoute Workforce — Current Schedule — {date:yyyy-MM-dd}",
                $"As of {roster.AsOfUtc:O}",
                $"Staffing: {roster.Staffing.FilledPositions}/{roster.Staffing.RequiredPositions} required; vacancies={roster.Staffing.Vacancies}; status={(roster.Staffing.MeetsMinimums ? "MEETS MINIMUMS" : "DEFICIENT")}",
                string.Empty
            };
            foreach (var item in roster.Assignments.OrderBy(x => stationNames.GetValueOrDefault(x.StationId, x.StationId.ToString("D"))).ThenBy(x => x.ShiftName).ThenBy(x => x.Position))
            {
                var station = stationNames.GetValueOrDefault(item.StationId, item.StationId.ToString("D"));
                var unit = item.UnitId is Guid uid ? unitNames.GetValueOrDefault(uid, uid.ToString("D")) : "—";
                lines.Add($"{station,-8} {unit,-10} {item.ShiftName,-12} {item.Position,-16} {item.EmployeeName,-24} {item.Qualification,-16} [{item.State}/{item.AssignmentType}]");
            }
            if (roster.Staffing.Warnings.Count > 0)
            {
                lines.Add(string.Empty); lines.Add("STAFFING WARNINGS");
                lines.AddRange(roster.Staffing.Warnings.Select(x => "- " + x));
            }
            if (includeChanges)
            {
                lines.Add(string.Empty); lines.Add("SAME-DAY CHANGES");
                lines.AddRange(roster.Changes.Select(x => $"{x.OccurredAtUtc:O} {x.Kind} {x.Summary} — {x.Actor}"));
            }
            var content = string.Join(Environment.NewLine, lines);
            return new WorkforceRosterPrintDto(agencyId, date, roster.AsOfUtc, includeChanges ? "Current Schedule + Changes" : "Current Schedule", content, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant());
        }
    }

    private WorkforceAssignmentDto[] CurrentAssignments(Guid agencyId, DateOnly date, DateTimeOffset asOfUtc) => _workforceAssignments.Values
        .Where(x => x.AgencyId == agencyId && x.Date == date && x.EffectiveAtUtc <= asOfUtc && (x.EndsAtUtc is null || x.EndsAtUtc > asOfUtc) && (IsFilledState(x.State) || x.State == "Open"))
        .OrderBy(x => x.ShiftName).ThenBy(x => x.StationId).ThenBy(x => x.Position).ToArray();

    private bool HasQualification(WorkforceAssignmentDto assignment, string requiredQualification, DateOnly date)
    {
        if (string.IsNullOrWhiteSpace(requiredQualification)) return true;
        if (assignment.Qualification.Split([',', ';', '/'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Any(x => x.Equals(requiredQualification, StringComparison.OrdinalIgnoreCase))) return true;
        return _qualifications.Values.Any(x => x.AgencyId == assignment.AgencyId && x.State.Equals("Active", StringComparison.OrdinalIgnoreCase)
            && x.Qualification.Equals(requiredQualification, StringComparison.OrdinalIgnoreCase)
            && x.EffectiveDate <= date && (x.ExpiresDate is null || x.ExpiresDate >= date)
            && ((!string.IsNullOrWhiteSpace(assignment.EmployeeId) && x.PersonId.Equals(assignment.EmployeeId, StringComparison.OrdinalIgnoreCase)) || x.PersonName.Equals(assignment.EmployeeName, StringComparison.OrdinalIgnoreCase)));
    }

    private void RecordWorkforceChange(Guid agencyId, DateOnly date, Guid? assignmentId, string kind, string summary, string actor)
    {
        var now = DateTimeOffset.UtcNow;
        _workforceScheduleChanges.Add(new WorkforceScheduleChangeDto(Guid.NewGuid(), agencyId, date, assignmentId, kind, summary, actor, now));
        _workforceChangeHistory.Add($"{now:O} {kind} {summary} by {actor}");
    }

    private static bool IsFilledState(string state) => state is "Assigned" or "Acting" or "Detail" or "Overtime" or "Training";
    private static string NormalizeAssignmentState(string value) => value.Trim().ToLowerInvariant() switch
    {
        "assigned" => "Assigned", "acting" => "Acting", "detail" => "Detail", "overtime" => "Overtime", "training" => "Training",
        "open" => "Open", "callout" or "call-out" => "CallOut", "relieved" => "Relieved", _ => throw new ArgumentException($"Unsupported workforce assignment state '{value}'.")
    };
    private static string NormalizeAssignmentType(string value) => string.IsNullOrWhiteSpace(value) ? "Regular" : value.Trim();
    private static string AssignmentSummary(WorkforceAssignmentDto item) => $"{item.Date:yyyy-MM-dd} {item.ShiftName} {item.Position} {item.EmployeeName} state={item.State} type={item.AssignmentType}";
    private static string ComputeRosterHash(Guid agencyId, DateOnly date, int revision, IReadOnlyList<WorkforceAssignmentDto> assignments, WorkforceStaffingEvaluationDto staffing)
    {
        var canonical = new StringBuilder().Append(agencyId.ToString("D")).Append('|').Append(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('|').Append(revision).Append('|');
        foreach (var item in assignments.OrderBy(x => x.Id)) canonical.Append(item.Id.ToString("D")).Append(':').Append(item.UpdatedAtUtc.ToUniversalTime().ToString("O")).Append(';');
        canonical.Append(staffing.RequiredPositions).Append('|').Append(staffing.FilledPositions).Append('|').Append(staffing.Vacancies);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()))).ToLowerInvariant();
    }
}
