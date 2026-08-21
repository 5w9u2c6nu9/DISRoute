namespace DISRoute.Contracts.Operational;

public sealed record RotationDto(Guid Id, Guid AgencyId, string Name, string Pattern, DateOnly EffectiveDate, bool Enabled, string UpdatedBy, DateTimeOffset UpdatedAtUtc);
public sealed record UpsertRotationRequest(Guid AgencyId, string Name, string Pattern, DateOnly EffectiveDate, bool Enabled, string Actor);
public sealed record OvertimeOfferDto(Guid Id, Guid AgencyId, DateOnly Date, string ShiftName, Guid StationId, string Position, string Qualification, int Slots, string State, IReadOnlyList<string> AcceptedEmployees, DateTimeOffset CreatedAtUtc);
public sealed record CreateOvertimeOfferRequest(Guid AgencyId, DateOnly Date, string ShiftName, Guid StationId, string Position, string Qualification, int Slots);
public sealed record AcceptOvertimeRequest(string EmployeeName);
public sealed record TimesheetEntryDto(Guid Id, Guid AgencyId, string EmployeeName, DateOnly WorkDate, decimal Hours, string TimeCategory, Guid? StationId, Guid? IncidentId, string Status, string? ApprovedBy, string Notes, DateTimeOffset UpdatedAtUtc);
public sealed record UpsertTimesheetEntryRequest(Guid AgencyId, string EmployeeName, DateOnly WorkDate, decimal Hours, string TimeCategory, Guid? StationId, Guid? IncidentId, string Notes);
public sealed record ApproveTimesheetRequest(string Actor, bool Approved);
public sealed record PayrollExportDto(Guid AgencyId, DateOnly FromDate, DateOnly ToDate, DateTimeOffset GeneratedAtUtc, IReadOnlyList<PayrollLineDto> Lines);
public sealed record PayrollLineDto(string EmployeeName, string TimeCategory, decimal Hours);
public sealed record DailyRosterDto(Guid AgencyId, DateOnly Date, DateTimeOffset AsOfUtc, IReadOnlyList<WorkforceShiftDto> Assignments, IReadOnlyList<string> ChangeHistory);

public sealed record StaffingRuleDto(Guid Id, Guid AgencyId, Guid? StationId, string ShiftName, string Position, int MinimumCount, string RequiredQualification, bool Enabled, string UpdatedBy, DateTimeOffset UpdatedAtUtc);
public sealed record UpsertStaffingRuleRequest(Guid AgencyId, Guid? StationId, string ShiftName, string Position, int MinimumCount, string RequiredQualification, bool Enabled);
public sealed record WorkforceAssignmentDto(Guid Id, Guid AgencyId, DateOnly Date, string ShiftName, Guid StationId, Guid? UnitId, string Position, string? EmployeeId, string EmployeeName, string Qualification, string AssignmentType, string State, DateTimeOffset EffectiveAtUtc, DateTimeOffset? EndsAtUtc, string Source, string UpdatedBy, DateTimeOffset UpdatedAtUtc);
public sealed record UpsertWorkforceAssignmentRequest(Guid AgencyId, DateOnly Date, string ShiftName, Guid StationId, Guid? UnitId, string Position, string? EmployeeId, string EmployeeName, string Qualification, string AssignmentType, string State, DateTimeOffset? EffectiveAtUtc, DateTimeOffset? EndsAtUtc, string Source);
public sealed record WorkforceReliefRequest(string? EmployeeId, string EmployeeName, string Qualification, string AssignmentType, string? Notes);
public sealed record WorkforceActionRequest(string? Notes);
public sealed record WorkforceScheduleChangeDto(Guid Id, Guid AgencyId, DateOnly Date, Guid? AssignmentId, string Kind, string Summary, string Actor, DateTimeOffset OccurredAtUtc);
public sealed record StaffingRuleResultDto(Guid RuleId, string Scope, string ShiftName, string Position, string RequiredQualification, int RequiredCount, int FilledCount, int Vacancies, IReadOnlyList<string> AssignedEmployees);
public sealed record WorkforceStaffingEvaluationDto(Guid AgencyId, DateOnly Date, DateTimeOffset AsOfUtc, int RequiredPositions, int FilledPositions, int Vacancies, bool MeetsMinimums, IReadOnlyList<StaffingRuleResultDto> Rules, IReadOnlyList<string> Warnings);
public sealed record WorkforceRosterSnapshotDto(Guid Id, Guid AgencyId, DateOnly Date, int Revision, string State, DateTimeOffset PostedAtUtc, string PostedBy, string Notes, string ContentSha256, IReadOnlyList<WorkforceAssignmentDto> Assignments, WorkforceStaffingEvaluationDto Staffing);
public sealed record PublishWorkforceRosterRequest(string? Notes);
public sealed record WorkforceRosterViewDto(Guid AgencyId, DateOnly Date, DateTimeOffset AsOfUtc, IReadOnlyList<WorkforceAssignmentDto> Assignments, WorkforceRosterSnapshotDto? PostedRoster, WorkforceStaffingEvaluationDto Staffing, IReadOnlyList<WorkforceScheduleChangeDto> Changes);
public sealed record WorkforceRosterPrintDto(Guid AgencyId, DateOnly Date, DateTimeOffset AsOfUtc, string Title, string Content, string ContentSha256);
public sealed record WorkforceRosterPrintResultDto(Guid OperationId, Guid AgencyId, DateOnly Date, string State, string? ProviderReference, string? Error, DateTimeOffset CompletedAtUtc);

public sealed record ApprovalWorkflowDto(Guid Id, Guid AgencyId, string EntityType, Guid EntityId, string Name, string Mode, string State, int CurrentSequence, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
public sealed record ApprovalStepDto(Guid Id, Guid WorkflowId, int Sequence, string Approver, bool Required, string State, string? Comment, DateTimeOffset? DecidedAtUtc);
public sealed record CreateApprovalWorkflowRequest(Guid AgencyId, string EntityType, Guid EntityId, string Name, string Mode, IReadOnlyList<ApprovalStepDefinition> Steps);
public sealed record ApprovalStepDefinition(int Sequence, string Approver, bool Required);
public sealed record DecideApprovalStepRequest(string Actor, string Decision, string? Comment);

public sealed record ViolationDto(Guid Id, Guid AgencyId, Guid PremiseId, Guid? InspectionId, string Code, string Description, string Severity, string State, DateOnly IssuedDate, DateOnly? DueDate, DateOnly? ResolvedDate);
public sealed record CreateViolationRequest(Guid AgencyId, Guid PremiseId, Guid? InspectionId, string Code, string Description, string Severity, DateOnly IssuedDate, DateOnly? DueDate);
public sealed record HazmatRecordDto(Guid Id, Guid AgencyId, Guid PremiseId, string Material, string UnNumber, string Quantity, string StorageLocation, string Hazards, string State, DateTimeOffset UpdatedAtUtc);
public sealed record UpsertHazmatRecordRequest(Guid AgencyId, Guid PremiseId, string Material, string UnNumber, string Quantity, string StorageLocation, string Hazards, string State);
public sealed record InvestigationDto(Guid Id, Guid AgencyId, Guid? IncidentId, Guid? PremiseId, string Title, string LeadInvestigator, string State, string Summary, DateTimeOffset OpenedAtUtc, DateTimeOffset UpdatedAtUtc, DateTimeOffset? ClosedAtUtc);
public sealed record CreateInvestigationRequest(Guid AgencyId, Guid? IncidentId, Guid? PremiseId, string Title, string LeadInvestigator, string Summary);
public sealed record PersonnelQualificationDto(Guid Id, Guid AgencyId, string PersonId, string PersonName, string Qualification, DateOnly EffectiveDate, DateOnly? ExpiresDate, string State, string Source, DateTimeOffset UpdatedAtUtc);
public sealed record UpsertPersonnelQualificationRequest(Guid AgencyId, string PersonId, string PersonName, string Qualification, DateOnly EffectiveDate, DateOnly? ExpiresDate, string State, string Source);
