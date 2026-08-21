namespace DISRoute.Application.Security;

public static class Permissions
{
    public const string IncidentView = "incident.view";
    public const string IncidentCreate = "incident.create";
    public const string IncidentEdit = "incident.edit";
    public const string IncidentClose = "incident.close";
    public const string IncidentDispatch = "incident.dispatch";
    public const string IncidentSupervisorOverride = "incident.supervisor-override";
    public const string UnitView = "unit.view";
    public const string UnitAssign = "unit.assign";
    public const string UnitChangeStatus = "unit.change-status";
    public const string StationView = "station.view";
    public const string StationAcknowledge = "station.acknowledge";
    public const string StationConfigure = "station.configure";
    public const string GisView = "gis.view";
    public const string GisEditOperational = "gis.edit-operational";
    public const string PremiseView = "premise.view";
    public const string PremiseEdit = "premise.edit";
    public const string PreplanPublish = "preplan.publish";
    public const string MessagingUse = "messaging.use";
    public const string AlertManage = "alert.manage";
    public const string WorkforceView = "workforce.view";
    public const string WorkforceManage = "workforce.manage";
    public const string WorkforceSelfService = "workforce.self-service";
    public const string DocumentsView = "documents.view";
    public const string DocumentsManage = "documents.manage";
    public const string DocumentsSign = "documents.sign";
    public const string RmsView = "rms.view";
    public const string RmsEdit = "rms.edit";
    public const string NerisSubmit = "neris.submit";
    public const string PersonnelView = "personnel.view";
    public const string Export = "data.export";
    public const string SecurityAdmin = "security.admin";
    public const string SystemAdmin = "system.admin";
    public const string ProviderAdmin = "provider.admin";
    public const string ModuleAdmin = "module.admin";
    public const string AuditView = "audit.view";
    public const string BreakGlass = "security.break-glass";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        IncidentView, IncidentCreate, IncidentEdit, IncidentClose, IncidentDispatch, IncidentSupervisorOverride,
        UnitView, UnitAssign, UnitChangeStatus, StationView, StationAcknowledge, StationConfigure,
        GisView, GisEditOperational, PremiseView, PremiseEdit, PreplanPublish, MessagingUse, AlertManage,
        WorkforceView, WorkforceManage, WorkforceSelfService, DocumentsView, DocumentsManage, DocumentsSign,
        RmsView, RmsEdit, NerisSubmit, PersonnelView, Export, SecurityAdmin, SystemAdmin, ProviderAdmin,
        ModuleAdmin, AuditView, BreakGlass
    };
}
