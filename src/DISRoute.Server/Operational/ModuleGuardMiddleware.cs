namespace DISRoute.Server.Operational;

/// <summary>Enforces per-agency module enablement at the HTTP command/read boundary.</summary>
public sealed class ModuleGuardMiddleware
{
    private readonly RequestDelegate _next;
    private static readonly (PathString Prefix, string Module)[] Routes =
    [
        (new PathString("/api/gis"), "gis"),
        (new PathString("/api/map"), "gis"),
        (new PathString("/api/premises"), "gis"),
        (new PathString("/api/preplans"), "gis"),
        (new PathString("/api/hydrants"), "gis"),
        (new PathString("/api/station"), "station"),
        (new PathString("/api/mobile"), "mobile"),
        (new PathString("/api/messaging"), "messaging"),
        (new PathString("/api/messages"), "messaging"),
        (new PathString("/api/workforce"), "workforce"),
        (new PathString("/api/documents"), "documents"),
        (new PathString("/api/forms"), "documents"),
        (new PathString("/api/rms"), "rms"),
        (new PathString("/api/video"), "cameras"),
        (new PathString("/api/vessels"), "marine")
    ];

    public ModuleGuardMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, OperationalStore store)
    {
        foreach (var route in Routes)
        {
            if (!context.Request.Path.StartsWithSegments(route.Prefix)) continue;
            var module = store.Modules.FirstOrDefault(x => x.Key.Equals(route.Module, StringComparison.OrdinalIgnoreCase));
            if (module is { Enabled: false })
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.WriteAsJsonAsync(new { error = $"The {module.DisplayName} module is disabled for this agency.", code = "module_disabled", module = module.Key });
                return;
            }
        }
        await _next(context);
    }
}
