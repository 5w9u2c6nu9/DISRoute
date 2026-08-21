using DISRoute.Integrations;
using DISRoute.Contracts.Operational;
using DISRoute.Server.Operational;
using DISRoute.Server.Realtime;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.HttpOverrides;
using System.Net;
using System.Threading.RateLimiting;

var windowsServiceMode = args.Any(x => x.Equals("--windows-service", StringComparison.OrdinalIgnoreCase));
if (windowsServiceMode && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")))
{
    Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", Environments.Production);
    Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", Environments.Production);
}

var builder = WebApplication.CreateBuilder(args);
var externalConfigFile = Environment.GetEnvironmentVariable("DISROUTE_CONFIG_FILE")?.Trim();
if (!string.IsNullOrWhiteSpace(externalConfigFile))
    builder.Configuration.AddJsonFile(Path.GetFullPath(externalConfigFile), optional: false, reloadOnChange: true);
builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    var certificatePath = builder.Configuration["Security:Tls:CertificatePath"]?.Trim();
    if (!string.IsNullOrWhiteSpace(certificatePath))
    {
        var password = SecretReferenceResolver.ResolveOptional(builder.Configuration, "Security:Tls:CertificatePassword");
        var port = Math.Clamp(builder.Configuration.GetValue("Security:Tls:Port", 5443), 1, 65535);
        options.ListenAnyIP(port, listen => listen.UseHttps(Path.GetFullPath(certificatePath), password));
    }
});
if (builder.Environment.IsProduction() || builder.Configuration.GetValue("Logging:JsonConsole", false))
{
    builder.Logging.ClearProviders();
    builder.Logging.AddJsonConsole(options => options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ");
}

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    var permitLimit = Math.Clamp(builder.Configuration.GetValue("Security:RateLimit:PermitLimitPerMinute", 600), 1, 100_000);
    var queueLimit = Math.Clamp(builder.Configuration.GetValue("Security:RateLimit:QueueLimit", 0), 0, 1000);
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var identity = context.User.Identity?.IsAuthenticated == true
            ? context.User.Identity.Name ?? "authenticated"
            : context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(identity, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            QueueLimit = queueLimit,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            Window = TimeSpan.FromMinutes(1),
            AutoReplenishment = true
        });
    });
});

builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme).AddNegotiate();
builder.Services.AddSingleton<LdapGroupResolver>();
builder.Services.AddTransient<Microsoft.AspNetCore.Authentication.IClaimsTransformation, DISRouteRoleClaimsTransformation>();
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
});
builder.Services.AddSignalR();
builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var registry = new ProviderRegistry();
    if (sp.GetRequiredService<IWebHostEnvironment>().IsDevelopment())
    {
        registry.Register(new SimulationStationAlertProvider());
        registry.Register(new SimulationPrintProvider());
        registry.Register(new SimulationRoutingProvider());
        registry.Register(new SimulationFireReportingProvider());
        registry.Register(new SimulationRecordingProvider());
        registry.Register(new SimulationCameraDiscoveryProvider());
        registry.Register(new SimulationRadioProvider());
    }
    var stationUrl = config["Integrations:StationAlert:Endpoint"];
    if (Uri.TryCreate(stationUrl, UriKind.Absolute, out var stationEndpoint))
        registry.Register(new HttpJsonStationAlertProvider(new HttpProviderOptions("http-station-alert", "HTTP Station Alert", "Station", stationEndpoint, SecretReferenceResolver.ResolveOptional(config, "Integrations:StationAlert:BearerToken"))));
    var reportUrl = config["Integrations:FireReporting:Endpoint"];
    if (Uri.TryCreate(reportUrl, UriKind.Absolute, out var reportEndpoint))
        registry.Register(new HttpJsonFireReportingProvider(new HttpProviderOptions("http-fire-reporting", "HTTP Fire Reporting", "Reporting", reportEndpoint, SecretReferenceResolver.ResolveOptional(config, "Integrations:FireReporting:BearerToken"))));
    var spool = config["Integrations:Printing:SpoolDirectory"];
    if (!string.IsNullOrWhiteSpace(spool)) registry.Register(new FileSpoolPrintProvider(spool));
    return registry;
});
builder.Services.AddSingleton<OperationalMutationBuffer>();
builder.Services.AddSingleton<AuthoritativeCommandGate>();
builder.Services.AddSingleton<DurableIdempotencyStore>();
builder.Services.AddSingleton<OperationalStore>();
builder.Services.AddSingleton<FeatureStore>();
builder.Services.AddSingleton<ReplayService>();
builder.Services.AddSingleton<RmsReportingService>();
builder.Services.AddSingleton<NerisSubmissionService>();
// Persistence starts first so PostgreSQL migrations/state are loaded before HA election or background work.
builder.Services.AddSingleton<StatePersistenceService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<StatePersistenceService>());
builder.Services.AddSingleton<HaCoordinatorService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<HaCoordinatorService>());
builder.Services.AddSingleton<DurableMutationWriterService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DurableMutationWriterService>());
builder.Services.AddHostedService<PostgreSqlOutboxPublisherService>();
builder.Services.AddHostedService<PostgreSqlRealtimeRelayService>();
builder.Services.AddHostedService<DispatchDeliveryWorker>();
builder.Services.AddHostedService<AlertEscalationWorker>();
builder.Services.AddHostedService<NerisSubmissionWorker>();
builder.Services.AddSingleton<MfaService>();
builder.Services.AddSingleton<ExportPackageService>();
builder.Services.AddSingleton<BackupRecoveryService>();
builder.Services.AddSingleton<DurabilityCommitFilter>();
builder.Services.AddSingleton<OperationalReadinessService>();

var app = builder.Build();
var requireAuthentication = app.Configuration.GetValue("Security:RequireAuthentication", true);
var requireHttps = app.Configuration.GetValue("Security:RequireHttps", !app.Environment.IsDevelopment());
var maxRequestBodyBytes = app.Configuration.GetValue<long?>("Security:MaxRequestBodyBytes") ?? 10 * 1024 * 1024;
if (app.Environment.IsProduction() && !requireAuthentication) throw new InvalidOperationException("Production DISRoute cannot start with authentication disabled.");
if (app.Environment.IsProduction() && !requireHttps) throw new InvalidOperationException("Production DISRoute cannot start with HTTPS enforcement disabled.");
if (app.Environment.IsProduction() && app.Configuration.GetValue("Qualification:EnableCrashInjection", false))
    throw new InvalidOperationException("Qualification crash injection cannot be enabled in Production.");
ProductionDeploymentSafety.ValidateStartup(app.Configuration, app.Environment);
if (app.Environment.IsProduction()) _ = app.Services.GetRequiredService<MfaService>();

if (app.Configuration.GetValue("Security:ReverseProxy:Enabled", false))
{
    var forwarded = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
        ForwardLimit = Math.Clamp(app.Configuration.GetValue("Security:ReverseProxy:ForwardLimit", 1), 1, 5)
    };
    forwarded.KnownProxies.Clear();
    forwarded.KnownIPNetworks.Clear();
    foreach (var proxy in app.Configuration.GetSection("Security:ReverseProxy:KnownProxies").Get<string[]>() ?? [])
        if (IPAddress.TryParse(proxy, out var address)) forwarded.KnownProxies.Add(address);
    foreach (var network in app.Configuration.GetSection("Security:ReverseProxy:KnownNetworks").Get<string[]>() ?? [])
    {
        if (System.Net.IPNetwork.TryParse(network, out var parsedNetwork)) forwarded.KnownIPNetworks.Add(parsedNetwork);
    }
    app.UseForwardedHeaders(forwarded);
}

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";
    context.Response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";
    await next();
});
if (app.Environment.IsProduction()) app.UseHsts();
if (requireHttps) app.UseHttpsRedirection();
app.Use(async (context, next) =>
{
    var bodyFeature = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
    if (bodyFeature is { IsReadOnly: false }) bodyFeature.MaxRequestBodySize = maxRequestBodyBytes;
    await next();
});
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.Use(async (context, next) =>
{
    var degraded = context.User.FindFirst("disroute:identity-degraded")?.Value;
    if (context.User.Identity?.IsAuthenticated == true && !string.IsNullOrWhiteSpace(degraded))
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.Headers.CacheControl = "no-store";
        await context.Response.WriteAsJsonAsync(new
        {
            error = "Required identity group resolution is unavailable.",
            detail = degraded
        });
        return;
    }
    await next();
});
app.UseMiddleware<MfaSessionMiddleware>();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api")) context.Response.Headers.CacheControl = "no-store";
    var requireMutationHeader = context.RequestServices.GetRequiredService<IConfiguration>().GetValue("Security:RequireMutationHeader", !app.Environment.IsDevelopment());
    var write = !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method) && !HttpMethods.IsOptions(context.Request.Method);
    if (requireMutationHeader && write && context.Request.Path.StartsWithSegments("/api") && !string.Equals(context.Request.Headers["X-DISRoute-Request"], "1", StringComparison.Ordinal))
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new { error = "Missing DISRoute mutation guard header." });
        return;
    }
    await next();
});
app.UseMiddleware<PrimaryWriteGuardMiddleware>();
app.UseMiddleware<ModuleGuardMiddleware>();
app.UseMiddleware<PermissionGuardMiddleware>();

app.MapGet("/health", (OperationalStore store, IConfiguration configuration) => Results.Ok(new SystemStatusDto(
    "DISRoute.Server", "healthy", configuration["Persistence:Provider"] ?? "InMemory", "SignalR",
    store.GetIncidents(openOnly: true).Count, OperationsHub.ConnectedClients, DateTimeOffset.UtcNow))).AllowAnonymous();

app.MapGet("/health/live", () => Results.Ok(new
{
    service = "DISRoute.Server",
    status = "alive",
    asOfUtc = DateTimeOffset.UtcNow
})).AllowAnonymous();

app.MapGet("/health/ready", (StatePersistenceService persistence, DurableMutationWriterService journal, HaCoordinatorService ha) =>
{
    var persistenceStatus = persistence.Status;
    var journalStatus = journal.Status;
    var haStatus = ha.Status;
    var journalLag = Math.Max(0, journalStatus.Queued - journalStatus.Persisted);
    var ready = string.Equals(persistenceStatus.State, "Healthy", StringComparison.OrdinalIgnoreCase)
        && journalLag == 0
        && journalStatus.PendingAuthoritativeBatches == 0
        && haStatus.AcceptingWrites;
    var payload = new
    {
        service = "DISRoute.Server",
        status = ready ? "ready" : "not-ready",
        checks = new
        {
            persistenceHealthy = string.Equals(persistenceStatus.State, "Healthy", StringComparison.OrdinalIgnoreCase),
            migrationIntegrityVerified = persistenceStatus.MigrationIntegrityVerified,
            mutationJournalDrained = journalLag == 0 && journalStatus.PendingAuthoritativeBatches == 0,
            authoritativeWriter = haStatus.AcceptingWrites
        },
        asOfUtc = DateTimeOffset.UtcNow
    };
    return Results.Json(payload, statusCode: ready ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
}).AllowAnonymous();

var operationsHub = app.MapHub<OperationsHub>("/hubs/operations");
if (!requireAuthentication) operationsHub.AllowAnonymous();

var api = app.MapGroup("/api");
api.AddEndpointFilter<DurabilityCommitFilter>();
if (!requireAuthentication) api.AllowAnonymous();

api.MapFeatureEndpoints();

api.MapGet("/me", (HttpContext context) =>
{
    var name = context.User.Identity?.Name;
    return Results.Ok(new
    {
        userId = name ?? "local-development",
        displayName = name ?? "Local Development User",
        authenticated = context.User.Identity?.IsAuthenticated == true,
        agencyId = OperationalStore.DefaultAgencyId,
        roles = context.User.Claims.Where(c => c.Type is System.Security.Claims.ClaimTypes.Role or "role" or "roles").SelectMany(c => c.Value.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
    });
});

api.MapGet("/persistence/status", (StatePersistenceService persistence) => Results.Ok(persistence.Status));
api.MapGet("/persistence/journal-status", (DurableMutationWriterService journal) => Results.Ok(journal.Status));
api.MapGet("/ha/node", (HaCoordinatorService ha) => Results.Ok(ha.Status));
api.MapGet("/admin/deployment/status", (OperationalReadinessService readiness) => Results.Ok(readiness.GetDeploymentReadiness()));
api.MapGet("/admin/upgrade/preflight", (OperationalReadinessService readiness) =>
{
    var result = readiness.GetUpgradePreflight();
    return Results.Json(result, statusCode: result.SafeToUpgrade ? StatusCodes.Status200OK : StatusCodes.Status409Conflict);
});
api.MapPost("/persistence/flush", async (StatePersistenceService persistence, CancellationToken cancellationToken) =>
{
    await persistence.FlushAsync(cancellationToken);
    return Results.Ok(persistence.Status);
});

api.MapGet("/persistence/backups", async (BackupRecoveryService backups, CancellationToken cancellationToken) =>
    Results.Ok(await backups.ListAsync(cancellationToken)));
api.MapPost("/persistence/backups", async (CreatePortableBackupRequest request, BackupRecoveryService backups, CancellationToken cancellationToken) =>
{
    try { return Results.Ok(await backups.CreateAsync(request, cancellationToken)); }
    catch (Exception ex) { return ApiError(ex); }
});
api.MapGet("/persistence/backups/{fileName}/verify", async (string fileName, BackupRecoveryService backups, CancellationToken cancellationToken) =>
{
    try
    {
        var result = await backups.VerifyAsync(fileName, cancellationToken);
        return result.Valid ? Results.Ok(result) : Results.Json(result, statusCode: StatusCodes.Status422UnprocessableEntity);
    }
    catch (Exception ex) { return ApiError(ex); }
});
api.MapPost("/persistence/backups/{fileName}/stage-restore", async (string fileName, StageRestoreRequest request, BackupRecoveryService backups, CancellationToken cancellationToken) =>
{
    try { return Results.Ok(await backups.StageRestoreAsync(fileName, request, cancellationToken)); }
    catch (Exception ex) { return ApiError(ex); }
});

api.MapGet("/agencies", (OperationalStore store) => Results.Ok(new[] { store.Agency }));
api.MapGet("/stations", (OperationalStore store) => Results.Ok(store.Stations));
api.MapGet("/units", (OperationalStore store) => Results.Ok(store.Units));
api.MapGet("/capabilities", (OperationalStore store) => Results.Ok(store.Capabilities));
api.MapGet("/modules", (OperationalStore store) => Results.Ok(store.Modules));
api.MapPut("/modules/{key}", async (string key, SetModuleStateRequest request, OperationalStore store, IHubContext<OperationsHub> hub) =>
{
    try
    {
        var updated = store.SetModule(key, request.Enabled);
        await hub.Clients.Group($"agency:{OperationalStore.DefaultAgencyId:D}").SendAsync("moduleChanged", updated);
        return Results.Ok(updated);
    }
    catch (Exception ex) { return ApiError(ex); }
});

api.MapGet("/incidents", (bool? openOnly, string? query, OperationalStore store) => Results.Ok(store.GetIncidents(openOnly ?? false, query)));
api.MapGet("/incidents/open", (OperationalStore store) => Results.Ok(store.GetIncidents(openOnly: true)));
api.MapGet("/incidents/{id:guid}", (Guid id, OperationalStore store) =>
{
    try { return Results.Ok(store.GetIncident(id)); }
    catch (Exception ex) { return ApiError(ex); }
});
api.MapGet("/incidents/{id:guid}/activity", (Guid id, OperationalStore store) =>
{
    try { return Results.Ok(store.GetActivity(id)); }
    catch (Exception ex) { return ApiError(ex); }
});
api.MapPost("/incidents", async (CreateIncidentRequest request, HttpContext context, OperationalStore store, IHubContext<OperationsHub> hub) =>
{
    try
    {
        var created = store.CreateIncident(request, Actor(context));
        await hub.Clients.Group($"agency:{created.AgencyId:D}").SendAsync("incidentChanged", created);
        return Results.Created($"/api/incidents/{created.Id:D}", created);
    }
    catch (Exception ex) { return ApiError(ex); }
});
api.MapPut("/incidents/{id:guid}", async (Guid id, UpdateIncidentRequest request, HttpContext context, OperationalStore store, IHubContext<OperationsHub> hub) =>
{
    try
    {
        var updated = store.UpdateIncident(id, request, Actor(context));
        await BroadcastIncident(hub, updated);
        return Results.Ok(updated);
    }
    catch (Exception ex) { return ApiError(ex); }
});
api.MapPost("/incidents/{id:guid}/assign", async (Guid id, AssignUnitRequest request, HttpContext context, OperationalStore store, IHubContext<OperationsHub> hub) =>
{
    try
    {
        var updated = store.AssignUnit(id, request, Actor(context));
        await BroadcastIncident(hub, updated);
        await hub.Clients.Group($"agency:{updated.AgencyId:D}").SendAsync("unitsChanged", store.Units);
        return Results.Ok(updated);
    }
    catch (Exception ex) { return ApiError(ex); }
});
api.MapPost("/incidents/{id:guid}/close", async (Guid id, CloseIncidentRequest request, HttpContext context, OperationalStore store, IHubContext<OperationsHub> hub) =>
{
    try
    {
        var updated = store.CloseIncident(id, request, Actor(context));
        await BroadcastIncident(hub, updated);
        await hub.Clients.Group($"agency:{updated.AgencyId:D}").SendAsync("unitsChanged", store.Units);
        return Results.Ok(updated);
    }
    catch (Exception ex) { return ApiError(ex); }
});
api.MapPost("/incidents/{id:guid}/reopen", async (Guid id, ReopenIncidentRequest request, HttpContext context, OperationalStore store, IHubContext<OperationsHub> hub) =>
{
    try
    {
        var updated = store.ReopenIncident(id, request, Actor(context));
        await BroadcastIncident(hub, updated);
        return Results.Ok(updated);
    }
    catch (Exception ex) { return ApiError(ex); }
});
api.MapPost("/incidents/{id:guid}/narrative", async (Guid id, AddNarrativeRequest request, HttpContext context, OperationalStore store, IHubContext<OperationsHub> hub) =>
{
    try
    {
        var activity = store.AddNarrative(id, request, Actor(context));
        await hub.Clients.Group($"incident:{id:D}").SendAsync("incidentActivity", activity);
        return Results.Ok(activity);
    }
    catch (Exception ex) { return ApiError(ex); }
});

api.MapPost("/units/{id:guid}/status", async (Guid id, SetUnitStatusRequest request, HttpContext context, OperationalStore store, IHubContext<OperationsHub> hub) =>
{
    try
    {
        var updated = store.SetUnitStatus(id, request, Actor(context));
        await hub.Clients.Group($"agency:{updated.AgencyId:D}").SendAsync("unitChanged", updated);
        return Results.Ok(updated);
    }
    catch (Exception ex) { return ApiError(ex); }
});

api.MapGet("/gis/road-restrictions", (OperationalStore store) => Results.Ok(store.RoadRestrictions));
api.MapPost("/gis/road-restrictions", async (CreateRoadRestrictionRequest request, HttpContext context, OperationalStore store, IHubContext<OperationsHub> hub) =>
{
    try
    {
        var created = store.CreateRoadRestriction(request, Actor(context));
        await hub.Clients.Group($"agency:{created.AgencyId:D}").SendAsync("roadRestrictionChanged", created);
        return Results.Created($"/api/gis/road-restrictions/{created.Id:D}", created);
    }
    catch (Exception ex) { return ApiError(ex); }
});
api.MapPost("/gis/road-restrictions/{id:guid}/end", async (Guid id, NotesActionRequest request, OperationalStore store, IHubContext<OperationsHub> hub) =>
{
    try
    {
        var updated = store.EndRoadRestriction(id, request.Actor, request.Notes);
        await hub.Clients.Group($"agency:{updated.AgencyId:D}").SendAsync("roadRestrictionChanged", updated);
        return Results.Ok(updated);
    }
    catch (Exception ex) { return ApiError(ex); }
});

api.MapGet("/messages", (Guid? incidentId, OperationalStore store) => Results.Ok(incidentId is Guid id ? store.Messages.Where(x => x.IncidentId == id).ToArray() : store.Messages));
api.MapPost("/messages", async (SendMessageRequest request, OperationalStore store, IHubContext<OperationsHub> hub) =>
{
    try
    {
        var created = store.SendMessage(request);
        await hub.Clients.Group($"agency:{created.AgencyId:D}").SendAsync("messageReceived", created);
        if (created.IncidentId is Guid iid) await hub.Clients.Group($"incident:{iid:D}").SendAsync("messageReceived", created);
        return Results.Ok(created);
    }
    catch (Exception ex) { return ApiError(ex); }
});

api.MapGet("/workforce/shifts", (OperationalStore store) => Results.Ok(store.Workforce));
api.MapPost("/workforce/shifts", (CreateWorkforceShiftRequest request, OperationalStore store) =>
{
    try { return Results.Ok(store.CreateShift(request)); }
    catch (Exception ex) { return ApiError(ex); }
});

api.MapGet("/documents", (OperationalStore store) => Results.Ok(store.Documents));
api.MapPost("/documents", (CreateDocumentRequest request, HttpContext context, OperationalStore store) =>
{
    try { return Results.Ok(store.CreateDocument(request with { CreatedBy = Actor(context) })); }
    catch (Exception ex) { return ApiError(ex); }
});
api.MapPost("/documents/{id:guid}/publish", (Guid id, PublishDocumentRequest request, HttpContext context, OperationalStore store) =>
{
    try { return Results.Ok(store.PublishDocument(id, request with { Actor = Actor(context) })); }
    catch (Exception ex) { return ApiError(ex); }
});

api.MapGet("/rms/records", (OperationalStore store) => Results.Ok(store.RmsRecords));
api.MapPost("/rms/records", (CreateRmsRecordRequest request, OperationalStore store) =>
{
    try { return Results.Ok(store.CreateRmsRecord(request)); }
    catch (Exception ex) { return ApiError(ex); }
});

api.MapGet("/rms/incident-reports", (Guid? incidentId, OperationalStore store) => Results.Ok(incidentId is Guid iid ? store.RmsIncidentReports.Where(x => x.IncidentId == iid).ToArray() : store.RmsIncidentReports));
api.MapGet("/rms/incident-reports/{id:guid}", (Guid id, OperationalStore store) => { try { return Results.Ok(store.GetRmsIncidentReport(id)); } catch (Exception ex) { return ApiError(ex); } });
api.MapGet("/rms/incident-reports/{id:guid}/versions", (Guid id, OperationalStore store) => { try { return Results.Ok(store.GetRmsReportVersions(id)); } catch (Exception ex) { return ApiError(ex); } });
api.MapGet("/rms/incident-reports/{id:guid}/attachments", (Guid id, OperationalStore store) => { try { return Results.Ok(store.GetRmsAttachments(id)); } catch (Exception ex) { return ApiError(ex); } });
api.MapGet("/rms/incident-reports/{id:guid}/validation", (Guid id, OperationalStore store) => { try { return Results.Ok(store.ValidateRmsIncidentReport(id)); } catch (Exception ex) { return ApiError(ex); } });
api.MapPost("/rms/incident-reports/from-incident/{incidentId:guid}", async (Guid incidentId, CreateRmsFromIncidentRequest request, HttpContext context, RmsReportingService reporting, IHubContext<OperationsHub> hub) =>
{
    try { var result = reporting.CreateFromIncident(incidentId, request, Actor(context)); await hub.Clients.Group($"agency:{result.AgencyId:D}").SendAsync("rmsReportChanged", result); return Results.Ok(result); } catch (Exception ex) { return ApiError(ex); }
});
api.MapPut("/rms/incident-reports/{id:guid}", async (Guid id, UpdateRmsIncidentReportRequest request, HttpContext context, OperationalStore store, IHubContext<OperationsHub> hub) =>
{
    try { var result = store.UpdateRmsIncidentReport(id, request, Actor(context)); await hub.Clients.Group($"agency:{result.AgencyId:D}").SendAsync("rmsReportChanged", result); return Results.Ok(result); } catch (Exception ex) { return ApiError(ex); }
});
api.MapPost("/rms/incident-reports/{id:guid}/refresh-cad", async (Guid id, RefreshRmsCadSnapshotRequest request, HttpContext context, RmsReportingService reporting, IHubContext<OperationsHub> hub) =>
{
    try { var result = reporting.RefreshFromCad(id, request, Actor(context)); await hub.Clients.Group($"agency:{result.AgencyId:D}").SendAsync("rmsReportChanged", result); return Results.Ok(result); } catch (Exception ex) { return ApiError(ex); }
});
api.MapPost("/rms/incident-reports/{id:guid}/submit-review", async (Guid id, SubmitRmsForReviewRequest request, HttpContext context, OperationalStore store, IHubContext<OperationsHub> hub) =>
{
    try { var result = store.SubmitRmsForReview(id, request, Actor(context)); await hub.Clients.Group($"agency:{result.AgencyId:D}").SendAsync("rmsReportChanged", result); return Results.Ok(result); } catch (Exception ex) { return ApiError(ex); }
});
api.MapPost("/rms/incident-reports/{id:guid}/review", async (Guid id, ReviewRmsIncidentReportRequest request, HttpContext context, OperationalStore store, IHubContext<OperationsHub> hub) =>
{
    try { var result = store.ReviewRmsIncidentReport(id, request, Actor(context)); await hub.Clients.Group($"agency:{result.AgencyId:D}").SendAsync("rmsReportChanged", result); return Results.Ok(result); } catch (Exception ex) { return ApiError(ex); }
});
api.MapPost("/rms/incident-reports/{id:guid}/amend", async (Guid id, AmendRmsIncidentReportRequest request, HttpContext context, OperationalStore store, IHubContext<OperationsHub> hub) =>
{
    try { var result = store.AmendRmsIncidentReport(id, request, Actor(context)); await hub.Clients.Group($"agency:{result.AgencyId:D}").SendAsync("rmsReportChanged", result); return Results.Ok(result); } catch (Exception ex) { return ApiError(ex); }
});
api.MapPost("/rms/incident-reports/{id:guid}/attachments", async (Guid id, AddRmsAttachmentReferenceRequest request, HttpContext context, OperationalStore store, IHubContext<OperationsHub> hub) =>
{
    try { var result = store.AddRmsAttachmentReference(id, request, Actor(context)); await hub.Clients.Group($"agency:{result.AgencyId:D}").SendAsync("rmsAttachmentChanged", result); return Results.Ok(result); } catch (Exception ex) { return ApiError(ex); }
});

api.MapGet("/rms/incident-reports/{id:guid}/neris-submissions", (Guid id, OperationalStore store) => Results.Ok(store.NerisSubmissions.Where(x => x.RmsReportId == id).OrderByDescending(x => x.RmsReportVersion).ToArray()));
api.MapPost("/rms/incident-reports/{id:guid}/neris/build", async (Guid id, BuildNerisSubmissionRequest request, HttpContext context, OperationalStore store, IHubContext<OperationsHub> hub) =>
{
    try { var result = store.BuildNerisSubmission(id, request, Actor(context)); await hub.Clients.Group($"agency:{result.AgencyId:D}").SendAsync("nerisSubmissionChanged", result); return Results.Ok(result); } catch (Exception ex) { return ApiError(ex); }
});
api.MapGet("/rms/neris-submissions", (Guid? rmsReportId, OperationalStore store) => Results.Ok(rmsReportId is Guid rid ? store.NerisSubmissions.Where(x => x.RmsReportId == rid).ToArray() : store.NerisSubmissions));
api.MapGet("/rms/neris-submissions/{id:guid}", (Guid id, OperationalStore store) => { try { return Results.Ok(store.GetNerisSubmission(id)); } catch (Exception ex) { return ApiError(ex); } });
api.MapGet("/rms/neris-submissions/{id:guid}/export", (Guid id, OperationalStore store) =>
{
    try
    {
        var submission = store.GetNerisSubmission(id);
        return Results.File(System.Text.Encoding.UTF8.GetBytes(submission.PayloadJson), "application/json", $"DISRoute-NERIS-{submission.IncidentId:D}-rmsv{submission.RmsReportVersion}-{submission.ProfileVersion}.json");
    }
    catch (Exception ex) { return ApiError(ex); }
});
api.MapGet("/rms/neris-submissions/{id:guid}/attempts", (Guid id, OperationalStore store) => { try { return Results.Ok(store.GetNerisSubmissionAttempts(id)); } catch (Exception ex) { return ApiError(ex); } });
api.MapPost("/rms/neris-submissions/{id:guid}/provider-validation", async (Guid id, string? providerKey, NerisSubmissionService service, CancellationToken cancellationToken) =>
{
    try { return Results.Ok(await service.ValidateProviderAsync(id, providerKey, cancellationToken)); } catch (Exception ex) { return ApiError(ex); }
});
api.MapPost("/rms/neris-submissions/{id:guid}/queue", async (Guid id, QueueNerisSubmissionRequest request, HttpContext context, OperationalStore store, IHubContext<OperationsHub> hub) =>
{
    try { var result = store.QueueNerisSubmission(id, request, Actor(context)); await hub.Clients.Group($"agency:{result.AgencyId:D}").SendAsync("nerisSubmissionChanged", result); return Results.Ok(result); } catch (Exception ex) { return ApiError(ex); }
});
api.MapPost("/rms/neris-submissions/{id:guid}/retry", async (Guid id, RetryNerisSubmissionRequest request, HttpContext context, OperationalStore store, IHubContext<OperationsHub> hub) =>
{
    try { var result = store.RetryNerisSubmission(id, request, Actor(context)); await hub.Clients.Group($"agency:{result.AgencyId:D}").SendAsync("nerisSubmissionChanged", result); return Results.Ok(result); } catch (Exception ex) { return ApiError(ex); }
});
api.MapPost("/rms/neris-submissions/{id:guid}/reconcile", async (Guid id, ReconcileNerisSubmissionRequest request, HttpContext context, OperationalStore store, IHubContext<OperationsHub> hub) =>
{
    try { var result = store.ReconcileNerisSubmission(id, request, Actor(context)); await hub.Clients.Group($"agency:{result.AgencyId:D}").SendAsync("nerisSubmissionChanged", result); return Results.Ok(result); } catch (Exception ex) { return ApiError(ex); }
});

api.MapGet("/search", (string q, OperationalStore store) => Results.Ok(store.Search(q)));
api.MapGet("/replay", (Guid? incidentId, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, OperationalStore store) => Results.Ok(store.GetReplay(incidentId, fromUtc, toUtc)));
api.MapGet("/replay/incidents/{id:guid}/snapshot", (Guid id, DateTimeOffset? atUtc, ReplayService replay) =>
{
    try { return Results.Ok(replay.GetIncidentSnapshot(id, atUtc)); }
    catch (Exception ex) { return ApiError(ex); }
});
api.MapGet("/incidents/{id:guid}/after-action-package", (Guid id, ExportPackageService exports) =>
{
    try { return Results.File(exports.BuildAfterActionPackage(id), "application/zip", $"DISRoute-AfterAction-{id:D}.zip"); }
    catch (Exception ex) { return ApiError(ex); }
});
api.MapGet("/records/package", (DateTimeOffset? fromUtc, DateTimeOffset? toUtc, ExportPackageService exports) => Results.File(exports.BuildRecordsPackage(fromUtc, toUtc), "application/zip", $"DISRoute-Records-{DateTime.UtcNow:yyyyMMddHHmmss}.zip"));
api.MapGet("/admin/support-bundle", (ExportPackageService exports) => Results.File(exports.BuildSupportBundle(), "application/zip", $"DISRoute-Support-{DateTime.UtcNow:yyyyMMddHHmmss}.zip"));

if (windowsServiceMode && OperatingSystem.IsWindows())
    await WindowsServiceHost.RunAsync(app);
else
    await app.RunAsync();

static string Actor(HttpContext context) => context.User.Identity?.Name ?? "local-development";

static IResult ApiError(Exception ex) => ex switch
{
    KeyNotFoundException => Results.NotFound(new { error = ex.Message }),
    ArgumentException => Results.BadRequest(new { error = ex.Message }),
    InvalidOperationException when ex.Message.StartsWith("Concurrency conflict", StringComparison.Ordinal) => Results.Conflict(new { error = ex.Message }),
    InvalidOperationException => Results.BadRequest(new { error = ex.Message }),
    _ => Results.Problem(title: "DISRoute operation failed", detail: "Unexpected server error. Refer to server logs and the audit trail for diagnostic detail.")
};

static async Task BroadcastIncident(IHubContext<OperationsHub> hub, IncidentDto incident)
{
    await hub.Clients.Group($"agency:{incident.AgencyId:D}").SendAsync("incidentChanged", incident);
    await hub.Clients.Group($"incident:{incident.Id:D}").SendAsync("incidentChanged", incident);
}
