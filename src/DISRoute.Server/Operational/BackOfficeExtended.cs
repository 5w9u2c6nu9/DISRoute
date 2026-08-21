using DISRoute.Contracts.Operational;

namespace DISRoute.Server.Operational;

public sealed partial class FeatureStore
{
    private readonly Dictionary<Guid, RotationDto> _rotations = [];
    private readonly Dictionary<Guid, OvertimeOfferDto> _overtimeOffers = [];
    private readonly Dictionary<Guid, TimesheetEntryDto> _timesheets = [];
    private readonly Dictionary<Guid, ApprovalWorkflowDto> _approvalWorkflows = [];
    private readonly Dictionary<Guid, ApprovalStepDto> _approvalSteps = [];
    private readonly Dictionary<Guid, ViolationDto> _violations = [];
    private readonly Dictionary<Guid, HazmatRecordDto> _hazmat = [];
    private readonly Dictionary<Guid, InvestigationDto> _investigations = [];
    private readonly Dictionary<Guid, PersonnelQualificationDto> _qualifications = [];
    private readonly List<string> _workforceChangeHistory = [];

    public IReadOnlyList<RotationDto> Rotations { get { lock (_gate) return _rotations.Values.OrderBy(x => x.Name).ToArray(); } }
    public IReadOnlyList<OvertimeOfferDto> OvertimeOffers { get { lock (_gate) return _overtimeOffers.Values.OrderByDescending(x => x.Date).ToArray(); } }
    public IReadOnlyList<TimesheetEntryDto> Timesheets { get { lock (_gate) return _timesheets.Values.OrderByDescending(x => x.WorkDate).ToArray(); } }
    public IReadOnlyList<ApprovalWorkflowDto> ApprovalWorkflows { get { lock (_gate) return _approvalWorkflows.Values.OrderByDescending(x => x.UpdatedAtUtc).ToArray(); } }
    public IReadOnlyList<ApprovalStepDto> ApprovalSteps { get { lock (_gate) return _approvalSteps.Values.OrderBy(x => x.Sequence).ToArray(); } }
    public IReadOnlyList<ViolationDto> Violations { get { lock (_gate) return _violations.Values.OrderByDescending(x => x.IssuedDate).ToArray(); } }
    public IReadOnlyList<HazmatRecordDto> Hazmat { get { lock (_gate) return _hazmat.Values.OrderBy(x => x.Material).ToArray(); } }
    public IReadOnlyList<InvestigationDto> Investigations { get { lock (_gate) return _investigations.Values.OrderByDescending(x => x.OpenedAtUtc).ToArray(); } }
    public IReadOnlyList<PersonnelQualificationDto> Qualifications { get { lock (_gate) return _qualifications.Values.OrderBy(x => x.PersonName).ThenBy(x => x.Qualification).ToArray(); } }

    public RotationDto UpsertRotation(Guid? id, UpsertRotationRequest request)
    {
        lock (_gate)
        {
            RequireText(request.Name, "Rotation name"); RequireText(request.Pattern, "Rotation pattern");
            var key = id ?? Guid.NewGuid();
            var item = new RotationDto(key, request.AgencyId, request.Name.Trim(), request.Pattern.Trim(), request.EffectiveDate, request.Enabled, request.Actor, DateTimeOffset.UtcNow);
            _rotations[key] = item;
            _workforceChangeHistory.Add($"{DateTimeOffset.UtcNow:O} rotation {item.Name} {(id is null ? "created" : "updated")} by {request.Actor}");
            AuditMutation(item.AgencyId, request.Actor, id is null ? "RotationCreated" : "RotationUpdated", "Rotation", key.ToString("D"), item.Name);
            return item;
        }
    }

    public OvertimeOfferDto CreateOvertimeOffer(CreateOvertimeOfferRequest request, string actor)
    {
        lock (_gate)
        {
            if (request.Slots < 1) throw new ArgumentException("Overtime slots must be at least 1.");
            var station = _core.Stations.FirstOrDefault(x => x.Id == request.StationId) ?? throw new KeyNotFoundException("Station not found.");
            if (station.AgencyId != request.AgencyId) throw new InvalidOperationException("Overtime station belongs to another agency.");
            var item = new OvertimeOfferDto(Guid.NewGuid(), request.AgencyId, request.Date, request.ShiftName.Trim(), request.StationId, request.Position.Trim(), request.Qualification.Trim(), request.Slots, "Open", [], DateTimeOffset.UtcNow);
            _overtimeOffers[item.Id] = item;
            _workforceChangeHistory.Add($"{DateTimeOffset.UtcNow:O} overtime {item.Position} {item.Date:yyyy-MM-dd} opened by {actor}");
            AuditMutation(item.AgencyId, actor, "OvertimeOpened", "OvertimeOffer", item.Id.ToString("D"), $"{item.Position} slots={item.Slots}");
            return item;
        }
    }

    public OvertimeOfferDto AcceptOvertime(Guid id, AcceptOvertimeRequest request)
    {
        lock (_gate)
        {
            if (!_overtimeOffers.TryGetValue(id, out var current)) throw new KeyNotFoundException("Overtime offer not found.");
            if (current.State != "Open") throw new InvalidOperationException("Overtime offer is not open.");
            var accepted = current.AcceptedEmployees.ToList();
            if (!accepted.Contains(request.EmployeeName, StringComparer.OrdinalIgnoreCase)) accepted.Add(request.EmployeeName.Trim());
            var updated = current with { AcceptedEmployees = accepted, State = accepted.Count >= current.Slots ? "Filled" : "Open" };
            _overtimeOffers[id] = updated;
            _workforceChangeHistory.Add($"{DateTimeOffset.UtcNow:O} overtime {id:D} accepted by {request.EmployeeName}");
            AuditMutation(updated.AgencyId, request.EmployeeName, "OvertimeAccepted", "OvertimeOffer", id.ToString("D"), updated.State);
            return updated;
        }
    }

    public TimesheetEntryDto UpsertTimesheet(Guid? id, UpsertTimesheetEntryRequest request, string actor)
    {
        lock (_gate)
        {
            if (request.Hours <= 0 || request.Hours > 24) throw new ArgumentException("Timesheet hours must be greater than 0 and no more than 24.");
            if (request.StationId is Guid sid)
            {
                var station = _core.Stations.FirstOrDefault(x => x.Id == sid) ?? throw new KeyNotFoundException("Station not found.");
                if (station.AgencyId != request.AgencyId) throw new InvalidOperationException("Timesheet station belongs to another agency.");
            }
            if (request.IncidentId is Guid iid)
            {
                var incident = _core.GetIncident(iid);
                if (incident.AgencyId != request.AgencyId) throw new InvalidOperationException("Timesheet incident belongs to another agency.");
            }
            var key = id ?? Guid.NewGuid();
            var item = new TimesheetEntryDto(key, request.AgencyId, request.EmployeeName.Trim(), request.WorkDate, request.Hours, request.TimeCategory.Trim(), request.StationId, request.IncidentId, "Submitted", null, request.Notes.Trim(), DateTimeOffset.UtcNow);
            _timesheets[key] = item;
            AuditMutation(item.AgencyId, actor, id is null ? "TimesheetSubmitted" : "TimesheetUpdated", "Timesheet", key.ToString("D"), $"{item.EmployeeName} {item.Hours}h {item.TimeCategory}");
            return item;
        }
    }

    public TimesheetEntryDto ApproveTimesheet(Guid id, ApproveTimesheetRequest request)
    {
        lock (_gate)
        {
            if (!_timesheets.TryGetValue(id, out var current)) throw new KeyNotFoundException("Timesheet entry not found.");
            var updated = current with { Status = request.Approved ? "Approved" : "Rejected", ApprovedBy = request.Actor, UpdatedAtUtc = DateTimeOffset.UtcNow };
            _timesheets[id] = updated;
            AuditMutation(updated.AgencyId, request.Actor, request.Approved ? "TimesheetApproved" : "TimesheetRejected", "Timesheet", id.ToString("D"), updated.EmployeeName);
            return updated;
        }
    }

    public PayrollExportDto PayrollExport(Guid agencyId, DateOnly from, DateOnly to)
    {
        lock (_gate)
        {
            if (to < from) throw new ArgumentException("Payroll end date cannot precede start date.");
            var lines = _timesheets.Values.Where(x => x.AgencyId == agencyId && x.Status == "Approved" && x.WorkDate >= from && x.WorkDate <= to)
                .GroupBy(x => new { x.EmployeeName, x.TimeCategory })
                .Select(x => new PayrollLineDto(x.Key.EmployeeName, x.Key.TimeCategory, x.Sum(y => y.Hours)))
                .OrderBy(x => x.EmployeeName).ThenBy(x => x.TimeCategory).ToArray();
            return new PayrollExportDto(agencyId, from, to, DateTimeOffset.UtcNow, lines);
        }
    }

    public DailyRosterDto DailyRoster(Guid agencyId, DateOnly date)
    {
        lock (_gate)
        {
            var current = GetWorkforceRoster(agencyId, date);
            var assignments = current.Assignments.Count > 0
                ? current.Assignments.Select(x => new WorkforceShiftDto(x.Id, x.AgencyId, x.Date, x.ShiftName, x.StationId, x.Position, x.EmployeeName, x.Qualification, x.State)).ToArray()
                : _core.Workforce.Where(x => x.AgencyId == agencyId && x.Date == date).ToArray();
            var changes = current.Changes.Select(x => $"{x.OccurredAtUtc:O} {x.Kind} {x.Summary} by {x.Actor}").TakeLast(200).ToArray();
            return new DailyRosterDto(agencyId, date, current.AsOfUtc, assignments, changes);
        }
    }

    public ApprovalWorkflowDto CreateApprovalWorkflow(CreateApprovalWorkflowRequest request, string actor)
    {
        lock (_gate)
        {
            if (request.AgencyId == Guid.Empty) throw new ArgumentException("Agency is required.");
            if (string.IsNullOrWhiteSpace(request.EntityType)) throw new ArgumentException("Approval entity type is required.");
            if (string.IsNullOrWhiteSpace(request.Name)) throw new ArgumentException("Approval workflow name is required.");
            if (request.Steps.Count == 0) throw new ArgumentException("At least one approval step is required.");
            if (request.Steps.Any(x => x.Sequence <= 0 || string.IsNullOrWhiteSpace(x.Approver))) throw new ArgumentException("Approval steps require a positive sequence and approver.");
            var mode = request.Mode.Trim().Equals("Parallel", StringComparison.OrdinalIgnoreCase) ? "Parallel" : "Sequential";
            if (mode == "Sequential" && request.Steps.GroupBy(x => x.Sequence).Any(x => x.Count() > 1)) throw new ArgumentException("Sequential approval workflows require unique step sequence numbers.");
            ValidateApprovalTargetAgency(request.AgencyId, request.EntityType, request.EntityId);
            var now = DateTimeOffset.UtcNow;
            var workflow = new ApprovalWorkflowDto(Guid.NewGuid(), request.AgencyId, request.EntityType.Trim(), request.EntityId, request.Name.Trim(), mode, "Active", request.Steps.Min(x => x.Sequence), now, now);
            _approvalWorkflows[workflow.Id] = workflow;
            foreach (var definition in request.Steps.OrderBy(x => x.Sequence))
            {
                var active = mode == "Parallel" || definition.Sequence == workflow.CurrentSequence;
                var step = new ApprovalStepDto(Guid.NewGuid(), workflow.Id, definition.Sequence, definition.Approver.Trim(), definition.Required, active ? "Pending" : "Waiting", null, null);
                _approvalSteps[step.Id] = step;
            }
            AuditMutation(workflow.AgencyId, actor, "ApprovalWorkflowStarted", "ApprovalWorkflow", workflow.Id.ToString("D"), workflow.Name);
            return workflow;
        }
    }

    public ApprovalStepDto DecideApprovalStep(Guid stepId, DecideApprovalStepRequest request)
        => DecideApprovalStep(stepId, request, request.Actor);

    public ApprovalStepDto DecideApprovalStep(Guid stepId, DecideApprovalStepRequest request, string actor)
    {
        lock (_gate)
        {
            if (!_approvalSteps.TryGetValue(stepId, out var step)) throw new KeyNotFoundException("Approval step not found.");
            if (!_approvalWorkflows.TryGetValue(step.WorkflowId, out var workflow)) throw new KeyNotFoundException("Approval workflow not found.");
            if (step.State != "Pending") throw new InvalidOperationException("Approval step is not pending.");
            var decision = request.Decision.Trim().ToLowerInvariant() switch { "approve" or "approved" => "Approved", "deny" or "denied" => "Denied", "return" or "returned" => "Returned", _ => throw new ArgumentException("Decision must be Approve, Deny, or Return.") };
            var updatedStep = step with { State = decision, Comment = request.Comment, DecidedAtUtc = DateTimeOffset.UtcNow };
            _approvalSteps[stepId] = updatedStep;
            if (decision is "Denied" or "Returned")
            {
                _approvalWorkflows[workflow.Id] = workflow with { State = decision, UpdatedAtUtc = DateTimeOffset.UtcNow };
            }
            else
            {
                var allSteps = _approvalSteps.Values.Where(x => x.WorkflowId == workflow.Id).ToArray();
                if (workflow.Mode == "Sequential")
                {
                    var nextSequence = allSteps.Where(x => x.State == "Waiting").Select(x => (int?)x.Sequence).Min();
                    if (nextSequence is int seq)
                    {
                        foreach (var waiting in allSteps.Where(x => x.Sequence == seq && x.State == "Waiting")) _approvalSteps[waiting.Id] = waiting with { State = "Pending" };
                        _approvalWorkflows[workflow.Id] = workflow with { CurrentSequence = seq, UpdatedAtUtc = DateTimeOffset.UtcNow };
                    }
                    else _approvalWorkflows[workflow.Id] = workflow with { State = "Approved", UpdatedAtUtc = DateTimeOffset.UtcNow };
                }
                else if (allSteps.Where(x => x.Required).All(x => x.Id == stepId || x.State == "Approved"))
                    _approvalWorkflows[workflow.Id] = workflow with { State = "Approved", UpdatedAtUtc = DateTimeOffset.UtcNow };
            }
            AuditMutation(workflow.AgencyId, actor, "ApprovalStepDecided", "ApprovalStep", stepId.ToString("D"), decision);
            return updatedStep;
        }
    }

    public ViolationDto CreateViolation(CreateViolationRequest request, string actor)
    {
        lock (_gate)
        {
            if (!_premises.ContainsKey(request.PremiseId)) throw new KeyNotFoundException("Premise not found.");
            if (request.InspectionId is Guid inspectionId && !_inspections.ContainsKey(inspectionId)) throw new KeyNotFoundException("Inspection not found.");
            var item = new ViolationDto(Guid.NewGuid(), request.AgencyId, request.PremiseId, request.InspectionId, request.Code.Trim(), request.Description.Trim(), NormalizeSeverity(request.Severity), "Open", request.IssuedDate, request.DueDate, null);
            _violations[item.Id] = item; AuditMutation(item.AgencyId, actor, "ViolationIssued", "Violation", item.Id.ToString("D"), item.Code); return item;
        }
    }

    public ViolationDto ResolveViolation(Guid id, string actor)
    {
        lock (_gate)
        {
            if (!_violations.TryGetValue(id, out var current)) throw new KeyNotFoundException("Violation not found.");
            var updated = current with { State = "Resolved", ResolvedDate = DateOnly.FromDateTime(DateTime.UtcNow) };
            _violations[id] = updated; AuditMutation(updated.AgencyId, actor, "ViolationResolved", "Violation", id.ToString("D"), updated.Code); return updated;
        }
    }

    public HazmatRecordDto UpsertHazmat(Guid? id, UpsertHazmatRecordRequest request, string actor)
    {
        lock (_gate)
        {
            if (!_premises.ContainsKey(request.PremiseId)) throw new KeyNotFoundException("Premise not found.");
            var key = id ?? Guid.NewGuid();
            var item = new HazmatRecordDto(key, request.AgencyId, request.PremiseId, request.Material.Trim(), request.UnNumber.Trim(), request.Quantity.Trim(), request.StorageLocation.Trim(), request.Hazards.Trim(), request.State.Trim(), DateTimeOffset.UtcNow);
            _hazmat[key] = item; AuditMutation(item.AgencyId, actor, id is null ? "HazmatCreated" : "HazmatUpdated", "Hazmat", key.ToString("D"), item.Material); return item;
        }
    }

    public InvestigationDto CreateInvestigation(CreateInvestigationRequest request, string actor)
    {
        lock (_gate)
        {
            if (request.IncidentId is Guid iid) _core.GetIncident(iid);
            if (request.PremiseId is Guid pid && !_premises.ContainsKey(pid)) throw new KeyNotFoundException("Premise not found.");
            var now = DateTimeOffset.UtcNow;
            var item = new InvestigationDto(Guid.NewGuid(), request.AgencyId, request.IncidentId, request.PremiseId, request.Title.Trim(), request.LeadInvestigator.Trim(), "Open", request.Summary.Trim(), now, now, null);
            _investigations[item.Id] = item; AuditMutation(item.AgencyId, actor, "InvestigationOpened", "Investigation", item.Id.ToString("D"), item.Title); return item;
        }
    }

    public InvestigationDto CloseInvestigation(Guid id, string actor)
    {
        lock (_gate)
        {
            if (!_investigations.TryGetValue(id, out var current)) throw new KeyNotFoundException("Investigation not found.");
            var now = DateTimeOffset.UtcNow;
            var updated = current with { State = "Closed", UpdatedAtUtc = now, ClosedAtUtc = now };
            _investigations[id] = updated; AuditMutation(updated.AgencyId, actor, "InvestigationClosed", "Investigation", id.ToString("D"), updated.Title); return updated;
        }
    }

    public PersonnelQualificationDto UpsertQualification(Guid? id, UpsertPersonnelQualificationRequest request, string actor)
    {
        lock (_gate)
        {
            var key = id ?? Guid.NewGuid();
            var item = new PersonnelQualificationDto(key, request.AgencyId, request.PersonId.Trim(), request.PersonName.Trim(), request.Qualification.Trim(), request.EffectiveDate, request.ExpiresDate, request.State.Trim(), request.Source.Trim(), DateTimeOffset.UtcNow);
            _qualifications[key] = item; AuditMutation(item.AgencyId, actor, id is null ? "QualificationCreated" : "QualificationUpdated", "PersonnelQualification", key.ToString("D"), $"{item.PersonName} {item.Qualification}"); return item;
        }
    }
}
