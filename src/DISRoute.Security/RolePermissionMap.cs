using DISRoute.Application.Security;

namespace DISRoute.Security;

public static class RolePermissionMap
{
    private static readonly Dictionary<string, HashSet<string>> PermissionMap = new(StringComparer.OrdinalIgnoreCase)
    {
        [Roles.Dispatcher] = Set(Permissions.IncidentView, Permissions.IncidentCreate, Permissions.IncidentEdit, Permissions.IncidentClose, Permissions.IncidentDispatch, Permissions.UnitView, Permissions.UnitAssign, Permissions.UnitChangeStatus, Permissions.StationView, Permissions.GisView, Permissions.GisEditOperational, Permissions.PremiseView, Permissions.MessagingUse, Permissions.WorkforceView, Permissions.DocumentsView, Permissions.RmsView),
        [Roles.CallTaker] = Set(Permissions.IncidentView, Permissions.IncidentCreate, Permissions.IncidentEdit, Permissions.UnitView, Permissions.GisView, Permissions.PremiseView, Permissions.DocumentsView),
        [Roles.Police] = Set(Permissions.IncidentView, Permissions.UnitView, Permissions.UnitChangeStatus, Permissions.PersonnelView, Permissions.GisView, Permissions.MessagingUse),
        [Roles.Fire] = Set(Permissions.IncidentView, Permissions.UnitView, Permissions.UnitChangeStatus, Permissions.StationView, Permissions.StationAcknowledge, Permissions.GisView, Permissions.PremiseView, Permissions.MessagingUse, Permissions.WorkforceSelfService, Permissions.DocumentsView),
        [Roles.Ems] = Set(Permissions.IncidentView, Permissions.UnitView, Permissions.UnitChangeStatus, Permissions.StationView, Permissions.GisView, Permissions.PremiseView, Permissions.MessagingUse, Permissions.WorkforceSelfService, Permissions.DocumentsView),
        [Roles.Supervisor] = Set(Permissions.IncidentView, Permissions.IncidentCreate, Permissions.IncidentEdit, Permissions.IncidentClose, Permissions.IncidentDispatch, Permissions.IncidentSupervisorOverride, Permissions.UnitView, Permissions.UnitAssign, Permissions.UnitChangeStatus, Permissions.StationView, Permissions.StationAcknowledge, Permissions.GisView, Permissions.GisEditOperational, Permissions.PremiseView, Permissions.PremiseEdit, Permissions.PreplanPublish, Permissions.MessagingUse, Permissions.AlertManage, Permissions.WorkforceView, Permissions.WorkforceManage, Permissions.DocumentsView, Permissions.DocumentsManage, Permissions.DocumentsSign, Permissions.RmsView, Permissions.RmsEdit, Permissions.NerisSubmit, Permissions.PersonnelView, Permissions.Export, Permissions.AuditView),
        [Roles.StationOfficer] = Set(Permissions.IncidentView, Permissions.UnitView, Permissions.UnitChangeStatus, Permissions.StationView, Permissions.StationAcknowledge, Permissions.GisView, Permissions.PremiseView, Permissions.MessagingUse, Permissions.WorkforceView, Permissions.WorkforceSelfService, Permissions.DocumentsView, Permissions.DocumentsSign, Permissions.RmsView),
        [Roles.PreventionInspector] = Set(Permissions.IncidentView, Permissions.GisView, Permissions.PremiseView, Permissions.PremiseEdit, Permissions.DocumentsView, Permissions.DocumentsSign, Permissions.RmsView, Permissions.RmsEdit),
        [Roles.RecordsReporting] = Set(Permissions.IncidentView, Permissions.UnitView, Permissions.PremiseView, Permissions.DocumentsView, Permissions.RmsView, Permissions.RmsEdit, Permissions.NerisSubmit, Permissions.Export, Permissions.AuditView),
        [Roles.WorkforceAdmin] = Set(Permissions.IncidentView, Permissions.StationView, Permissions.WorkforceView, Permissions.WorkforceManage, Permissions.PersonnelView, Permissions.DocumentsView),
        [Roles.DmsAdmin] = Set(Permissions.DocumentsView, Permissions.DocumentsManage, Permissions.DocumentsSign, Permissions.RmsView, Permissions.AuditView),
        [Roles.Administrator] = new HashSet<string>(Permissions.All, StringComparer.OrdinalIgnoreCase)
    };

    public static HashSet<string> ForRoles(IEnumerable<string> roles)
    {
        var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var role in roles)
            if (PermissionMap.TryGetValue(role, out var rolePermissions)) permissions.UnionWith(rolePermissions);
        return permissions;
    }

    public static bool HasPermission(IEnumerable<string> roles, string permission) => ForRoles(roles).Contains(permission);

    private static HashSet<string> Set(params string[] permissions) => new(permissions, StringComparer.OrdinalIgnoreCase);
}
