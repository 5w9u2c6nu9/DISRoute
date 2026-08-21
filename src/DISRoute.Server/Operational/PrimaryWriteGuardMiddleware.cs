namespace DISRoute.Server.Operational;

public sealed class PrimaryWriteGuardMiddleware
{
    private readonly RequestDelegate _next;
    public PrimaryWriteGuardMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, HaCoordinatorService ha)
    {
        var write = !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method) && !HttpMethods.IsOptions(context.Request.Method);
        var localHaControl = context.Request.Path.StartsWithSegments("/api/admin/ha/local");
        if (write && context.Request.Path.StartsWithSegments("/api") && !localHaControl && !ha.AcceptingWrites)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.Headers["Retry-After"] = "2";
            await context.Response.WriteAsJsonAsync(new
            {
                error = "This DISRoute server is fenced/read-only and is not the active Dispatch Primary.",
                node = ha.Status.NodeId,
                role = ha.Status.Role,
                state = ha.Status.State
            });
            return;
        }
        await _next(context);
    }
}
