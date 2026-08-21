using System.Security.Claims;
using DISRoute.Application.Security;
using DISRoute.Security;

namespace DISRoute.Server.Operational;

public sealed class PermissionGuardMiddleware
{
    private readonly RequestDelegate _next;
    public PermissionGuardMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, IConfiguration configuration)
    {
        if (!context.Request.Path.StartsWithSegments("/api") || !configuration.GetValue("Security:RequireAuthentication", true))
        {
            await _next(context);
            return;
        }

        var required = RequiredPermission(context.Request.Method, context.Request.Path.Value ?? string.Empty);
        if (required is null)
        {
            await _next(context);
            return;
        }

        var roles = context.User.Claims
            .Where(x => x.Type is ClaimTypes.Role or "role" or "roles")
            .SelectMany(x => x.Value.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (!RolePermissionMap.HasPermission(roles, required))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { error = "Permission denied.", permission = required });
            return;
        }

        await _next(context);
    }

    private static string? RequiredPermission(string method, string path)
    {
        var write = !HttpMethods.IsGet(method) && !HttpMethods.IsHead(method) && !HttpMethods.IsOptions(method);
        if (path.Equals("/api/me", StringComparison.OrdinalIgnoreCase)) return null;
        if (path.StartsWith("/api/persistence/", StringComparison.OrdinalIgnoreCase)) return Permissions.SystemAdmin;
        if (path.StartsWith("/api/admin/", StringComparison.OrdinalIgnoreCase)) return write ? Permissions.SystemAdmin : Permissions.AuditView;
        if (path.StartsWith("/api/security/", StringComparison.OrdinalIgnoreCase)) return write ? Permissions.SecurityAdmin : Permissions.SecurityAdmin;
        if (path.StartsWith("/api/audit", StringComparison.OrdinalIgnoreCase)) return Permissions.AuditView;
        if (path.StartsWith("/api/replay", StringComparison.OrdinalIgnoreCase)) return Permissions.IncidentView;
        if (path.StartsWith("/api/modules", StringComparison.OrdinalIgnoreCase)) return write ? Permissions.ModuleAdmin : null;
        if (path.StartsWith("/api/providers", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/api/video/cameras", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/api/radio/streams", StringComparison.OrdinalIgnoreCase))
            return write ? Permissions.ProviderAdmin : null;
        if (path.StartsWith("/api/workforce", StringComparison.OrdinalIgnoreCase)) return write ? Permissions.WorkforceManage : Permissions.WorkforceView;
        if (path.StartsWith("/api/documents", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/api/forms", StringComparison.OrdinalIgnoreCase))
            return write ? Permissions.DocumentsManage : Permissions.DocumentsView;
        if (write && (path.StartsWith("/api/rms/neris-submissions", StringComparison.OrdinalIgnoreCase) || path.Contains("/neris/", StringComparison.OrdinalIgnoreCase))) return Permissions.NerisSubmit;
        if (write && path.Contains("/rms/neris", StringComparison.OrdinalIgnoreCase) && (path.EndsWith("/submit", StringComparison.OrdinalIgnoreCase) || path.EndsWith("/transport", StringComparison.OrdinalIgnoreCase))) return Permissions.NerisSubmit;
        if (path.StartsWith("/api/rms", StringComparison.OrdinalIgnoreCase)) return write ? Permissions.RmsEdit : Permissions.RmsView;
        if (path.StartsWith("/api/preplans", StringComparison.OrdinalIgnoreCase) && path.EndsWith("/publish", StringComparison.OrdinalIgnoreCase)) return Permissions.PreplanPublish;
        if (path.StartsWith("/api/premises", StringComparison.OrdinalIgnoreCase)) return write ? Permissions.PremiseEdit : Permissions.PremiseView;
        if (path.StartsWith("/api/gis", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/api/marine", StringComparison.OrdinalIgnoreCase)) return write ? Permissions.GisEditOperational : Permissions.GisView;
        if (path.StartsWith("/api/alerts", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/api/bulletins", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/api/mayday", StringComparison.OrdinalIgnoreCase)) return write ? Permissions.AlertManage : null;
        if (path.StartsWith("/api/station", StringComparison.OrdinalIgnoreCase)) return write ? Permissions.StationAcknowledge : Permissions.StationView;
        if (path.StartsWith("/api/messages", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/api/chat", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/api/messaging", StringComparison.OrdinalIgnoreCase)) return Permissions.MessagingUse;
        if (path.StartsWith("/api/avl", StringComparison.OrdinalIgnoreCase)) return write ? Permissions.UnitChangeStatus : Permissions.UnitView;
        if (path.StartsWith("/api/units", StringComparison.OrdinalIgnoreCase)) return write ? Permissions.UnitChangeStatus : Permissions.UnitView;
        if (path.Contains("/dispatch", StringComparison.OrdinalIgnoreCase)) return Permissions.IncidentDispatch;
        if (path.Contains("/assign", StringComparison.OrdinalIgnoreCase)) return Permissions.UnitAssign;
        if (path.Contains("/close", StringComparison.OrdinalIgnoreCase)) return Permissions.IncidentClose;
        if (path.Contains("/reopen", StringComparison.OrdinalIgnoreCase) || path.Contains("/narrative", StringComparison.OrdinalIgnoreCase) || path.Contains("/collaborative-narrative", StringComparison.OrdinalIgnoreCase)) return Permissions.IncidentEdit;
        if (path.StartsWith("/api/incidents", StringComparison.OrdinalIgnoreCase))
        {
            if (HttpMethods.IsPost(method) && path.Equals("/api/incidents", StringComparison.OrdinalIgnoreCase)) return Permissions.IncidentCreate;
            return write ? Permissions.IncidentEdit : Permissions.IncidentView;
        }
        return null;
    }
}
