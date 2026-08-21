namespace DISRoute.Server.Operational;

internal static class QualificationFaultInjection
{
    private const string HeaderName = "X-DISRoute-Qualification-Crash";

    public static void CrashIfRequested(HttpContext context, string point)
    {
        var environment = context.RequestServices.GetRequiredService<IHostEnvironment>();
        var configuration = context.RequestServices.GetRequiredService<IConfiguration>();
        if (!environment.IsDevelopment()) return;
        if (!configuration.GetValue("Qualification:EnableCrashInjection", false)) return;
        if (!string.Equals(context.Request.Headers[HeaderName], point, StringComparison.OrdinalIgnoreCase)) return;

        Environment.FailFast($"DISRoute PostgreSQL/HA qualification fault injected at '{point}'.");
    }
}
