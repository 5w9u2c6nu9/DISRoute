using DISRoute.Contracts.Operational;

namespace DISRoute.Server.Operational;

public sealed record RmsCadHandoffSeed(
    IncidentDto Incident,
    IReadOnlyList<RmsUnitSnapshotDto> Units,
    IReadOnlyList<RmsTimelineSnapshotDto> Timeline,
    IReadOnlyList<RmsCommandSnapshotDto> Commands,
    IReadOnlyList<RmsMutualAidSnapshotDto> MutualAid,
    string Narrative,
    DateTimeOffset CapturedAtUtc);

public sealed class RmsReportingService(OperationalStore core, FeatureStore features)
{
    public RmsCadHandoffSeed BuildSeed(Guid incidentId)
    {
        var incident = core.GetIncident(incidentId);
        var units = core.UnitHistory
            .Where(x => x.State.IncidentId == incidentId)
            .GroupBy(x => x.UnitId)
            .Select(g => g.OrderByDescending(x => x.RecordedAtUtc).First().State)
            .OrderBy(x => x.CallSign)
            .Select(x => new RmsUnitSnapshotDto(x.Id, x.StationId, x.CallSign, x.Discipline, x.UnitType, x.Status, x.UpdatedAtUtc))
            .ToArray();
        var timeline = core.GetActivity(incidentId)
            .Select(x => new RmsTimelineSnapshotDto(x.Id, x.Category, x.Message, x.Actor, x.OccurredAtUtc)).ToArray();
        var commands = features.Commands.Where(x => x.IncidentId == incidentId)
            .Select(x => new RmsCommandSnapshotDto(x.Id, x.CommandName, x.UnitId, x.Person, x.Actor, x.EffectiveAtUtc, x.EndedAtUtc)).ToArray();
        var mutualAid = features.MutualAid.Where(x => x.IncidentId == incidentId)
            .Select(x => new RmsMutualAidSnapshotDto(x.Id, x.RequestedAgency, x.ResourceType, x.Quantity, x.State, x.RequestedBy, x.RequestedAtUtc, x.UpdatedAtUtc)).ToArray();
        var shared = features.GetCollaborativeNarrative(incidentId).Text;
        var activityNarrative = string.Join(Environment.NewLine, timeline.Where(x => x.Category.Equals("NARRATIVE", StringComparison.OrdinalIgnoreCase)).Select(x => x.Message));
        var narrative = string.IsNullOrWhiteSpace(shared) ? activityNarrative : shared;
        return new RmsCadHandoffSeed(incident, units, timeline, commands, mutualAid, narrative, DateTimeOffset.UtcNow);
    }

    public RmsIncidentReportDto CreateFromIncident(Guid incidentId, CreateRmsFromIncidentRequest request, string actor) => core.CreateRmsIncidentReport(BuildSeed(incidentId), request, actor);
    public RmsIncidentReportDto RefreshFromCad(Guid reportId, RefreshRmsCadSnapshotRequest request, string actor)
    {
        var report = core.GetRmsIncidentReport(reportId);
        return core.RefreshRmsCadSnapshot(reportId, BuildSeed(report.IncidentId), request, actor);
    }
}
