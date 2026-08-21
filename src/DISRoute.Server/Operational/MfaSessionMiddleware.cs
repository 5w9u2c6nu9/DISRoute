using System.Security.Claims;

namespace DISRoute.Server.Operational;

public sealed record MfaSessionDto(string Token, string UserId, DateTimeOffset IssuedAtUtc, DateTimeOffset ExpiresAtUtc);
public sealed record MfaSessionValidationDto(bool Valid, string? UserId, DateTimeOffset? ExpiresAtUtc, string? Error);

/// <summary>
/// Enforces a second factor after the primary AD/OIDC identity has authenticated. OIDC identities
/// that already carry an amr=mfa claim are accepted; Windows/AD sessions use the signed, HttpOnly
/// DISRoute MFA session cookie issued by the TOTP verification endpoint.
/// </summary>
public sealed class MfaSessionMiddleware
{
    public const string CookieName = "DISRoute-MFA";
    private readonly RequestDelegate _next;
    public MfaSessionMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, IConfiguration configuration, FeatureStore store, MfaService mfa)
    {
        var requireMfa = configuration.GetValue("Security:RequireMfa", !context.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment());
        if (!requireMfa || context.User.Identity?.IsAuthenticated != true || IsExempt(context.Request.Path))
        {
            await _next(context);
            return;
        }

        var userId = context.User.Identity?.Name ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Authenticated identity has no stable user identifier." });
            return;
        }

        var amrSatisfied = context.User.FindAll("amr").Any(x => x.Value.Equals("mfa", StringComparison.OrdinalIgnoreCase) || x.Value.Equals("otp", StringComparison.OrdinalIgnoreCase));
        if (amrSatisfied) { await _next(context); return; }

        var enrolled = store.GetMfaCredential(userId);
        if (enrolled is null || enrolled.Pending)
        {
            context.Response.StatusCode = StatusCodes.Status428PreconditionRequired;
            await context.Response.WriteAsJsonAsync(new { error = "MFA enrollment is required before operational access.", code = "mfa_enrollment_required" });
            return;
        }

        if (!context.Request.Cookies.TryGetValue(CookieName, out var token))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "A current DISRoute MFA session is required.", code = "mfa_required" });
            return;
        }
        var validation = mfa.VerifySession(token, userId);
        if (!validation.Valid)
        {
            context.Response.Cookies.Delete(CookieName);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = validation.Error ?? "MFA session is invalid.", code = "mfa_required" });
            return;
        }

        await _next(context);
    }

    private static bool IsExempt(PathString path) =>
        path.StartsWithSegments("/health") ||
        path.StartsWithSegments("/api/me/totp");
}
