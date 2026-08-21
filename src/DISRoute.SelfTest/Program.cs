using DISRoute.Mobile.Core;
using DISRoute.Client.Common;
using DISRoute.Contracts.Operational;
using DISRoute.Server.Operational;
using Microsoft.Extensions.Configuration;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException("SELF-TEST FAILED: " + message);
}

Console.WriteLine("DISRoute self-test starting…");

foreach (var pair in new[]
{
    (Original: "", Edited: "initial narrative"),
    (Original: "alpha bravo", Edited: "alpha X bravo"),
    (Original: "alpha bravo charlie", Edited: "alpha charlie"),
    (Original: "alpha bravo charlie", Edited: "alpha DELTA charlie"),
    (Original: "same", Edited: "same")
})
{
    var patch = NarrativePatchBuilder.Build(pair.Original, pair.Edited);
    Check(patch.Apply(pair.Original) == pair.Edited, $"narrative patch round trip: {pair.Original} -> {pair.Edited}");
    Check(patch.HasChanges == (pair.Original != pair.Edited), "narrative patch change detection");
}

// Pass 25 seals the published PostgreSQL migration chain before live-database qualification.
// This is intentionally a file-level invariant test; database execution is covered by
// scripts/postgresql-ha-qualification.sh when a disposable PostgreSQL environment is available.
var migrationDirectoryCandidates = new[]
{
    Path.Combine(Directory.GetCurrentDirectory(), "db", "migrations"),
    Path.Combine(AppContext.BaseDirectory, "db", "migrations")
};
var migrationDirectory = migrationDirectoryCandidates.FirstOrDefault(Directory.Exists)
    ?? throw new DirectoryNotFoundException("SELF-TEST FAILED: published PostgreSQL migration directory was not found.");
var publishedMigrations = PostgreSqlDatabaseSafety.LoadPublishedMigrations(migrationDirectory);
Check(publishedMigrations.Count == 20, "published PostgreSQL migration count");
Check(publishedMigrations.Select(x => x.Version).SequenceEqual(Enumerable.Range(1, 20)), "published PostgreSQL migration sequence");
Check(publishedMigrations[^1].Version == 20, "published PostgreSQL migration latest schema version");
Check(publishedMigrations.All(x => x.Sha256.Length == 64 && x.Sha256.All(Uri.IsHexDigit)), "published PostgreSQL migration SHA-256 identities");
Check(publishedMigrations.Select(x => x.Sha256).Distinct(StringComparer.OrdinalIgnoreCase).Count() == publishedMigrations.Count, "published PostgreSQL migration SHA-256 uniqueness");
var wrappedMigration = publishedMigrations.First(x => x.Sql.TrimStart().StartsWith("BEGIN;", StringComparison.OrdinalIgnoreCase));
var unwrappedMigration = publishedMigrations.First(x => !x.Sql.TrimStart().StartsWith("BEGIN;", StringComparison.OrdinalIgnoreCase));
var wrappedTransactionBody = PostgreSqlDatabaseSafety.GetMigrationBodyForTransaction(wrappedMigration);
var unwrappedTransactionBody = PostgreSqlDatabaseSafety.GetMigrationBodyForTransaction(unwrappedMigration);
Check(!wrappedTransactionBody.StartsWith("BEGIN;", StringComparison.OrdinalIgnoreCase) && !wrappedTransactionBody.EndsWith("COMMIT;", StringComparison.OrdinalIgnoreCase), "wrapped migration outer transaction is delegated to Npgsql");
Check(wrappedTransactionBody.Length < wrappedMigration.Sql.Trim().Length, "wrapped migration body strips only its outer transaction wrapper");
Check(unwrappedTransactionBody == unwrappedMigration.Sql.Trim(), "unwrapped migration body is preserved for the Npgsql transaction");
var incompleteWrapperRejected = false;
try
{
    _ = PostgreSqlDatabaseSafety.GetMigrationBodyForTransaction(new PublishedPostgreSqlMigration(999, "999_invalid.sql", new string('0', 64), "BEGIN; SELECT 1;"));
}
catch (InvalidOperationException) { incompleteWrapperRejected = true; }
Check(incompleteWrapperRejected, "incomplete migration transaction wrapper rejected");


// Pass 26: physical PostgreSQL failover must never reuse a database-local advisory lock as if it
// were a shared fence across divergent timelines. The production topology contract fails closed.
{
    var unsafePhysical = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["HighAvailability:DatabaseTopology"] = "PhysicalFailover",
        ["HighAvailability:DatabaseFencingMode"] = "None"
    }).Build();
    var rejectedMissingWitness = false;
    try { _ = HaDatabaseTopologySafety.Resolve(unsafePhysical, externalWitnessConfigured: false); }
    catch (InvalidOperationException) { rejectedMissingWitness = true; }
    Check(rejectedMissingWitness, "physical PostgreSQL HA rejects missing independent witness");

    var rejectedMissingFencing = false;
    try { _ = HaDatabaseTopologySafety.Resolve(unsafePhysical, externalWitnessConfigured: true); }
    catch (InvalidOperationException) { rejectedMissingFencing = true; }
    Check(rejectedMissingFencing, "physical PostgreSQL HA rejects missing external DB fencing");

    var safePhysical = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["HighAvailability:DatabaseTopology"] = "PhysicalFailover",
        ["HighAvailability:DatabaseFencingMode"] = "External"
    }).Build();
    var policy = HaDatabaseTopologySafety.Resolve(safePhysical, externalWitnessConfigured: true);
    Check(policy.DatabaseTopology == HaDatabaseTopologySafety.PhysicalFailover && policy.RequiresExternalWitness && policy.RequiresExternalDatabaseFencing,
        "physical PostgreSQL HA accepts independent witness plus external fencing");
}

// PostgreSQL command-boundary correlation is exercised without requiring a live database here:
// mutations created inside one authoritative batch must remain pending until the state transaction
// marks that batch committed, and rejected batches must be removable without becoming authoritative.
var batchProbeQueue = new OperationalMutationBuffer();
var batchProbeIdempotency = new DurableIdempotencyStore();
var batchProbeCore = new OperationalStore(batchProbeQueue, batchProbeIdempotency);
var batchProbe = Guid.NewGuid();
using (batchProbeQueue.BeginAuthoritativeBatch(batchProbe))
{
    var probeStation = batchProbeCore.Stations[0];
    batchProbeCore.CreateIncident(new CreateIncidentRequest(OperationalStore.DefaultAgencyId, probeStation.Id, "Batch Probe", "Routine", "1 Atomic Way", "", "Test City", "Probe", "", "", "selftest:batch-probe"), "selftest");
}
Check(batchProbeQueue.PendingAuthoritativeBatchIds.SequenceEqual([batchProbe]), "authoritative mutation batch correlation");
batchProbeQueue.MarkAuthoritativeBatchesCommitted([batchProbe]);
Check(batchProbeQueue.PendingAuthoritativeBatchCount == 0, "authoritative mutation batch completion");
var abandonedProbe = Guid.NewGuid();
using (batchProbeQueue.BeginAuthoritativeBatch(abandonedProbe))
{
    var probeStation = batchProbeCore.Stations[0];
    batchProbeCore.CreateIncident(new CreateIncidentRequest(OperationalStore.DefaultAgencyId, probeStation.Id, "Rejected Batch Probe", "Routine", "2 Atomic Way", "", "Test City", "Probe", "", "", "selftest:batch-reject"), "selftest");
}
batchProbeQueue.AbandonBatch(abandonedProbe);
Check(batchProbeQueue.PendingAuthoritativeBatchCount == 0, "rejected authoritative batch abandonment");

var mutationQueue = new OperationalMutationBuffer();
var idempotency = new DurableIdempotencyStore();
var core = new OperationalStore(mutationQueue, idempotency);
var features = new FeatureStore(core, mutationQueue, idempotency);
var agency = OperationalStore.DefaultAgencyId;
var station = core.Stations[0];

var createRequest = new CreateIncidentRequest(agency, station.Id, "Structure Fire", "Emergency", "500 Test Avenue", "First / Second", "Test City", "Test Caller", "555-0199", "Smoke visible", "selftest:create");
var incident = core.CreateIncident(createRequest, "selftest");
Check(incident.Version == 1 && incident.Status == "Open", "incident creation");

var dupes = features.FindDuplicates(new DuplicateCheckRequest(agency, "Structure Fire", "500 Test Avenue", "555-0199", null, null));
Check(dupes.Any(x => x.IncidentId == incident.Id), "duplicate detection");

var recommendation = features.RecommendResponse(new ResponseRecommendationRequest(agency, incident.Id, incident.Nature, incident.Priority, station.Id, []));
Check(recommendation.Units.Count > 0, "response recommendation");
var unitIds = recommendation.Units.Take(2).Select(x => x.UnitId).ToArray();
foreach (var unitId in unitIds)
{
    incident = core.AssignUnit(incident.Id, new AssignUnitRequest(unitId, incident.Version, true, "selftest:assign:" + unitId), "selftest");
}
Check(core.Units.Count(x => x.IncidentId == incident.Id) == unitIds.Length, "unit assignment");

var dispatchRequest = new CommitDispatchRequest(incident.Version, unitIds, ["StationDisplay", "RunSheet", "StationAlert"], "selftest:dispatch");
var dispatch = features.CommitDispatch(incident.Id, dispatchRequest, "selftest");
Check(dispatch.Deliveries.Count == 3, "dispatch fan-out");
Check(features.StationDeliveries.Any(x => x.IncidentId == incident.Id), "station delivery creation");
Check(features.RunSheets.Any(x => x.IncidentId == incident.Id), "run-sheet creation");

var delivery = features.StationDeliveries.First(x => x.IncidentId == incident.Id);
features.AcknowledgeStationDelivery(delivery.Id, new StationAcknowledgeRequest("selftest-device", "selftest"));
Check(features.StationDeliveries.First(x => x.Id == delivery.Id).AckState == "Acknowledged", "station acknowledgement");
var stationPaths = features.GetStationDeliveryPaths(station.Id);
Check(stationPaths.Count(x => x.IncidentId == incident.Id) == 3, "station-scoped dispatch delivery paths");

var isolationStation = core.Stations.First(x => x.Id != station.Id);
var isolationUnit = core.Units.First(x => x.StationId == isolationStation.Id && x.IncidentId is null);
var isolationIncident = core.CreateIncident(
    new CreateIncidentRequest(agency, isolationStation.Id, "Station Isolation Probe", "Routine", "200 Isolation Way", "", "Test City", "Probe", "", "Cross-station delivery isolation", "selftest:station-isolation-create"),
    "selftest");
features.CommitDispatch(
    isolationIncident.Id,
    new CommitDispatchRequest(isolationIncident.Version, [isolationUnit.Id], ["StationDisplay", "RunSheet", "StationAlert"], "selftest:station-isolation-dispatch"),
    "selftest");
var primaryStationPathsAfterIsolationDispatch = features.GetStationDeliveryPaths(station.Id);
var isolationStationPaths = features.GetStationDeliveryPaths(isolationStation.Id);
Check(primaryStationPathsAfterIsolationDispatch.All(x => x.IncidentId != isolationIncident.Id), "station delivery path cross-station exclusion");
Check(isolationStationPaths.Count(x => x.IncidentId == isolationIncident.Id) == 3, "second-station dispatch delivery paths");
Check(isolationStationPaths.All(x => x.IncidentId != incident.Id), "station delivery path reverse cross-station exclusion");

var displayPath = dispatch.Deliveries.First(x => x.Provider == "StationDisplay");
features.CompleteDelivery(displayPath.Id, true, null, "selftest");
var confirmedRetryRejected = false;
try { features.RetryDelivery(displayPath.Id, new RetryDeliveryRequest("selftest")); }
catch (InvalidOperationException) { confirmedRetryRejected = true; }
Check(confirmedRetryRejected, "confirmed delivery retry rejection");
var alertPath = dispatch.Deliveries.First(x => x.Provider == "StationAlert");
features.CompleteDelivery(alertPath.Id, false, "self-test provider failure", "selftest");
var failedPath = features.GetStationDeliveryPaths(station.Id).First(x => x.Id == alertPath.Id);
Check(failedPath.State == "Failed" && failedPath.LastError == "self-test provider failure", "station delivery failure visibility");
var retriedPath = features.RetryDelivery(alertPath.Id, new RetryDeliveryRequest("selftest"));
Check(retriedPath.State == "Pending" && retriedPath.LastError is null, "failed station delivery retry");
features.CompleteDelivery(alertPath.Id, true, null, "selftest");
Check(features.GetStationDeliveryPaths(station.Id).First(x => x.Id == alertPath.Id).State == "Confirmed", "retried station delivery completion");
var runSheet = features.RunSheets.First(x => x.IncidentId == incident.Id);
var printedRunSheet = features.MarkRunSheetPrinted(runSheet.Id, "selftest");
var printedRunSheetRetry = features.MarkRunSheetPrinted(runSheet.Id, "selftest");
Check(printedRunSheet.Status == "Printed" && printedRunSheet.PrintedAtUtc == printedRunSheetRetry.PrintedAtUtc, "run-sheet print marking idempotency");

var fireWatch = features.CreateFireWatch(new CreateFireWatchRequest(agency, incident.Id, unitIds[0], "Supervisor", null, 30, "Self-test fire watch"), "selftest");
features.AddFireWatchCheck(fireWatch.Id, "selftest", "Conditions stable");
features.EndFireWatch(fireWatch.Id, "selftest");
Check(features.GetFireWatchChecks(fireWatch.Id).Count == 1, "fire watch checks");

var relocationDestination = core.Stations.First(x => x.Id != station.Id);
var relocation = features.CreateRelocation(new CreateRelocationRequest(agency, unitIds[0], relocationDestination.Id, "Self-test coverage"), "selftest");
Check(relocation.Status == "Active", "relocation creation");
relocation = features.EndRelocation(relocation.Id, "selftest");
Check(relocation.Status == "Completed" && relocation.EndedAtUtc is not null, "relocation completion");

var premise = features.UpsertPremise(null, new UpsertPremiseRequest(agency, "Test Building", "500 Test Avenue", "Commercial", "Front door", "Sprinkler", "Rear utility", "Hazard test", "Operational"), "selftest");
var preplan = features.CreatePreplan(new CreatePreplanRequest(agency, premise.Id, "Test Preplan", "Operational content", "selftest"), "selftest");
features.PublishPreplan(preplan.Id, "selftest");
features.RegisterCrg(new RegisterCrgAssetRequest(agency, premise.Id, "floor1.tif", "abc123", "1", "Floor", 1, 0, 1, 1, "selftest"), "selftest");
features.UpsertHydrant(null, new UpsertHydrantRequest(agency, "TEST-H1", "500 Test Avenue", 40.0, -74.0, 8, 60, 45, 1200, "InService"), "selftest");
Check(features.Preplans.Any(x => x.PremiseId == premise.Id && x.Status == "Published"), "preplan publication");

var map = features.CreateMapObject(new CreateMapObjectRequest(agency, incident.Id, "StagingArea", "Alpha staging", "{\"type\":\"Point\",\"coordinates\":[-74,40]}", "Incident", null), "selftest");
Check(map.Version == 1, "map object");
features.CreateMapObject(new CreateMapObjectRequest(agency, incident.Id, "IncidentLocation", "Incident scene", "{\"type\":\"Point\",\"coordinates\":[-74,40]}", "Incident", null), "selftest");
var restriction = core.CreateRoadRestriction(new CreateRoadRestrictionRequest(agency, "Self-test closure", "Road blocked for incident operations", 40.0, -74.0, 100, DateTimeOffset.UtcNow, null), "selftest");
var restrictionActiveAtUtc = DateTimeOffset.UtcNow;
var siteIntelligence = features.GetSiteIntelligence(incident.Id);
Check(siteIntelligence.Premises.Any(x => x.Id == premise.Id), "site-intelligence premise match");
Check(siteIntelligence.PublishedPreplans.Any(x => x.PremiseId == premise.Id), "site-intelligence published preplan");
Check(siteIntelligence.CrgAssets.Any(x => x.PremiseId == premise.Id && x.Floor == "1"), "site-intelligence CRG floor assets");
Check(siteIntelligence.Hydrants.Any(x => x.Hydrant.AssetId == "TEST-H1" && x.DistanceMeters is <= 1), "site-intelligence nearby hydrant");
Check(siteIntelligence.RoadRestrictions.Any(x => x.Restriction.Id == restriction.Id && x.DistanceMeters is <= 1), "site-intelligence road restriction proximity");
Check(siteIntelligence.SceneLatitude == 40.0 && siteIntelligence.SceneLongitude == -74.0, "site-intelligence scene coordinates");
restriction = core.EndRoadRestriction(restriction.Id, "selftest", "Road reopened");
Check(!restriction.Active && restriction.EndsAtUtc is not null, "road restriction lifecycle");
Check(features.GetSiteIntelligence(incident.Id).RoadRestrictions.All(x => x.Restriction.Id != restriction.Id), "ended road restriction removed from live site intelligence");
var route = features.CalculateRoute(new RouteRequest(agency, 40, -74, 40.01, -74.01, unitIds[0], incident.Id, true));
Check(route.DistanceMeters > 0, "route fallback");

var mayday = features.DeclareMayday(new DeclareMaydayRequest(agency, incident.Id, unitIds[0], "Self Test Member", "Floor 2", "Fireground", "selftest"));
var maydayAlert = features.Alerts.Single(x => x.CorrelationKey == $"mayday:{mayday.Id:D}");
Check(mayday.State == "Active" && maydayAlert.Severity == "Critical", "mayday critical alert");
mayday = features.ActOnMayday(mayday.Id, "acknowledge", "selftest-supervisor", new MaydayActionRequest(null, "Radio traffic confirmed"));
Check(mayday.State == "Acknowledged" && mayday.AcknowledgedBy == "selftest-supervisor" && mayday.AcknowledgedAtUtc is not null, "mayday acknowledgement");
mayday = features.ActOnMayday(mayday.Id, "assign", "selftest-supervisor", new MaydayActionRequest("Division Supervisor", "Accountability assigned"));
Check(mayday.State == "Assigned" && mayday.AssignedTo == "Division Supervisor", "mayday ownership assignment");
mayday = features.ActOnMayday(mayday.Id, "escalate", "selftest-supervisor", new MaydayActionRequest(null, "Second rescue team requested"));
Check(mayday.State == "Escalated" && mayday.EscalationLevel == 1, "mayday escalation");
Check(features.Alerts.Single(x => x.Id == maydayAlert.Id).State == "Claimed", "mayday alert ownership synchronized");

var group = features.CreateOperationalGroup(new CreateOperationalGroupRequest(agency, incident.Id, "Division 2", "Division", "Officer", "Fireground"), "selftest");
features.SetCommand(new SetCommandRequest(agency, incident.Id, "Incident Command", unitIds[0], "Chief", "selftest"));
var ma = features.CreateMutualAid(new CreateMutualAidRequest(agency, incident.Id, "Neighbor Agency", "Engine", 1, "selftest"));
features.UpdateMutualAid(ma.Id, "Accepted", "selftest");
Check(group.Status == "Active", "incident operations");

var leave = features.CreateLeave(new CreateLeaveRequest(agency, "Employee", DateOnly.FromDateTime(DateTime.Today.AddDays(7)), DateOnly.FromDateTime(DateTime.Today.AddDays(8)), "Vacation", "Test"));
features.ReviewLeave(leave.Id, new ReviewLeaveRequest("Supervisor", true, "Approved"));
Check(features.LeaveRequests.First(x => x.Id == leave.Id).Status == "Approved", "leave workflow");

var document = core.CreateDocument(new CreateDocumentRequest(agency, "Policy", "Internal", "Version one", "selftest"));
features.AddDocumentVersion(document.Id, "Version two", "selftest");
var acks = features.AssignDocumentAcknowledgements(document.Id, new AssignAcknowledgementRequest(["employee"], "selftest"));
features.AcknowledgeDocument(acks[0].Id, new AcknowledgeDocumentRequest("employee"));
Check(features.GetDocumentAcknowledgements(document.Id).Single().Status == "Acknowledged", "document acknowledgement");

var dmsDocument = features.CreateDmsDocument(new CreateDmsDocumentRequest(agency, "Self-test DMS Policy", "Internal", "Published baseline content"), "dms-admin");
var dmsRevision1 = features.GetDmsRevisions(dmsDocument.Id).Single();
Check(dmsRevision1.Version == 1 && dmsRevision1.State == "Draft" && dmsRevision1.ContentSha256.Length == 64, "DMS canonical draft revision and content hash");
var dmsAsset = features.AddDmsAssetReference(dmsDocument.Id, new AddDmsAssetReferenceRequest(1, "policy.pdf", "application/pdf", "dms://policies/selftest/policy-v1", new string('a', 64), "Internal"), "dms-admin");
Check(dmsAsset.DocumentVersion == 1 && dmsAsset.ContentSha256?.Length == 64, "DMS draft revision accepts immutable external asset reference metadata");
var unapprovedPublishRejected = false;
try { features.PublishDmsRevision(dmsDocument.Id, 1, new PublishDmsRevisionRequest(true), "dms-admin"); }
catch (InvalidOperationException) { unapprovedPublishRejected = true; }
Check(unapprovedPublishRejected, "DMS publication requires approved workflow");
var dmsWorkflow = features.StartDmsRevisionApproval(dmsDocument.Id, 1, new DmsApprovalRequest("DMS Publication", "Sequential", [new ApprovalStepDefinition(1, "Supervisor", true), new ApprovalStepDefinition(2, "Chief", true)]), "dms-admin");
var crossAgencyApprovalRejected = false;
try { features.CreateApprovalWorkflow(new CreateApprovalWorkflowRequest(Guid.NewGuid(), "DocumentVersion", dmsRevision1.Id, "Wrong Agency", "Sequential", [new ApprovalStepDefinition(1, "Supervisor", true)]), "selftest"); }
catch (InvalidOperationException) { crossAgencyApprovalRejected = true; }
Check(crossAgencyApprovalRejected, "approval workflow rejects known DMS target agency mismatch");
var dmsApproval1 = features.ApprovalSteps.Single(x => x.WorkflowId == dmsWorkflow.Id && x.Sequence == 1);
features.DecideApprovalStep(dmsApproval1.Id, new DecideApprovalStepRequest("ignored-client-actor", "Approve", "Reviewed"), "supervisor-authenticated");
var dmsApproval2 = features.ApprovalSteps.Single(x => x.WorkflowId == dmsWorkflow.Id && x.Sequence == 2);
features.DecideApprovalStep(dmsApproval2.Id, new DecideApprovalStepRequest("ignored-client-actor", "Approve", "Approved for publication"), "chief-authenticated");
dmsRevision1 = features.PublishDmsRevision(dmsDocument.Id, 1, new PublishDmsRevisionRequest(true), "dms-admin");
Check(dmsRevision1.State == "Published" && dmsRevision1.PublishedAtUtc is not null, "DMS approved immutable revision publication");
var publishedAssetMutationRejected = false;
try { features.AddDmsAssetReference(dmsDocument.Id, new AddDmsAssetReferenceRequest(1, "late.pdf", "application/pdf", "dms://policies/selftest/late", null, "Internal"), "dms-admin"); }
catch (InvalidOperationException) { publishedAssetMutationRejected = true; }
Check(publishedAssetMutationRejected, "published DMS revision asset package is immutable");
var dmsDistribution = features.CreateDmsDistribution(dmsDocument.Id, new CreateDmsDistributionRequest(1, "Role", "StationOfficer", true, DateTimeOffset.UtcNow.AddHours(24), ["reader-one", "reader-two"]), "dms-admin");
var dmsReadStatus = features.GetDmsRequiredReadStatus(dmsDocument.Id, 1);
Check(dmsDistribution.RequiredRead && dmsReadStatus.Assigned == 2 && dmsReadStatus.Pending == 2, "DMS role distribution creates version-specific required-read receipts");
features.CreateDmsDistribution(dmsDocument.Id, new CreateDmsDistributionRequest(1, "User", "reader-overdue", true, DateTimeOffset.UtcNow.AddMinutes(-1), ["reader-overdue"]), "dms-admin");
dmsReadStatus = features.GetDmsRequiredReadStatus(dmsDocument.Id, 1);
Check(dmsReadStatus.Assigned == 3 && dmsReadStatus.Overdue == 1, "DMS required-read overdue state is derived from distribution due time");
var dmsReceipt = dmsReadStatus.Receipts.Single(x => x.Recipient == "reader-one");
dmsReceipt = features.MarkDmsRead(dmsReceipt.Id, "reader-one");
dmsReceipt = features.AcknowledgeDmsRead(dmsReceipt.Id, "reader-one");
Check(dmsReceipt.State == "Acknowledged" && dmsReceipt.ReadAtUtc is not null && dmsReceipt.AcknowledgedAtUtc is not null, "DMS required-read read and acknowledgement lifecycle");
var wrongReaderRejected = false;
try { features.AcknowledgeDmsRead(dmsReadStatus.Receipts.Single(x => x.Recipient == "reader-two").Id, "not-reader-two"); }
catch (InvalidOperationException) { wrongReaderRejected = true; }
Check(wrongReaderRejected, "DMS required-read receipt cannot be acknowledged by another identity");
var dmsLink = features.LinkDmsDocument(dmsDocument.Id, new CreateDmsDocumentLinkRequest(1, "Incident", incident.Id.ToString("D"), "Operational reference"), "dms-admin");
Check(dmsLink.EntityType == "Incident" && features.GetDmsEntityLinks("Incident", incident.Id.ToString("D")).Any(x => x.Id == dmsLink.Id), "DMS entity linkage is bidirectionally queryable");
var dmsRevision2 = features.CreateDmsRevision(dmsDocument.Id, new CreateDmsRevisionRequest("Second revision content"), "dms-editor");
Check(features.GetDmsRevisions(dmsDocument.Id).Single(x => x.Version == 1).Content == "Published baseline content", "published DMS revision content remains immutable after new draft");
var dmsWorkflow2 = features.StartDmsRevisionApproval(dmsDocument.Id, 2, new DmsApprovalRequest("DMS Revision 2 Approval", "Sequential", [new ApprovalStepDefinition(1, "Supervisor", true)]), "dms-admin");
var dmsRevision2Step = features.ApprovalSteps.Single(x => x.WorkflowId == dmsWorkflow2.Id && x.State == "Pending");
features.DecideApprovalStep(dmsRevision2Step.Id, new DecideApprovalStepRequest("ignored", "Approve", null), "supervisor-authenticated");
dmsRevision2 = features.PublishDmsRevision(dmsDocument.Id, 2, new PublishDmsRevisionRequest(true), "dms-admin");
Check(dmsRevision2.State == "Published" && features.GetDmsRevisions(dmsDocument.Id).Single(x => x.Version == 1).State == "Superseded", "DMS newer publication supersedes prior revision without deleting history");
Check(features.GetDmsRequiredReadStatus(dmsDocument.Id, 1).Assigned == 3 && features.GetDmsRequiredReadStatus(dmsDocument.Id, 2).Assigned == 0, "DMS required-read history remains bound to the distributed revision");

var template = features.CreateFormTemplate(new CreateFormTemplateRequest(agency, "Test Form", "{\"fields\":[]}", "selftest"));
features.PublishFormTemplate(template.Id, "selftest");
var form = features.CreateForm(new CreateFormInstanceRequest(agency, template.Id, incident.Id, "{}", "selftest"));
features.SignForm(form.Id, new SignFormRequest("selftest"));
var formSignature = features.GetFormSignatures(form.Id).Single();
Check(features.Forms.First(x => x.Id == form.Id).Status == "Signed" && formSignature.ValuesSha256.Length == 64 && formSignature.TemplateVersion == template.Version, "form signature preserves immutable value/template hash metadata");

var inspection = features.CreateInspection(new CreateInspectionRequest(agency, premise.Id, "Annual", DateOnly.FromDateTime(DateTime.Today), "Inspector", "No deficiencies"));
features.CloseInspection(inspection.Id, "selftest");
var permit = features.CreatePermit(new CreatePermitRequest(agency, premise.Id, "Operational", "Holder", DateOnly.FromDateTime(DateTime.Today), null, "Test permit"), "selftest");
Check(permit.Number.StartsWith("PER-", StringComparison.Ordinal), "permit numbering");

var neris = features.UpsertNeris(null, new UpsertNerisReportRequest(agency, incident.Id, "{\"incident\":\"selftest\"}"), "selftest");
features.SubmitNeris(neris.Id, new SubmitNerisReportRequest("selftest"));
Check(features.NerisReports.First(x => x.Id == neris.Id).Status == "Submitted", "NERIS lifecycle");

var device = features.RegisterDevice(new RegisterDeviceRequest(agency, "selftest-device", "Self Test Station", "Station", "Windows", null), "selftest");
features.Heartbeat(new HeartbeatRequest(agency, device.DeviceId, "Station", "Windows", "1.0", "selftest", 0.2));
Check(features.Devices.First(x => x.DeviceId == device.DeviceId).LastSeenAtUtc is not null, "device heartbeat");

var workspace = features.SaveWorkspace(null, new SaveWorkspaceLayoutRequest(agency, "selftest", "Default", "Dispatcher", "SELFTEST", "Classic", "Compact", "{}", true));
Check(features.GetWorkspaces("selftest", "SELFTEST").Any(x => x.Id == workspace.Id), "roaming workspace");

var tempQueuePath = Path.Combine(Path.GetTempPath(), "disroute-selftest-" + Guid.NewGuid().ToString("N") + ".bin");
using (var protector = new AesGcmOfflineDataProtector(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)))
{
    var queue = new OfflineOperationStore(tempQueuePath, protector);
    var encryptedQueuedItem = queue.Enqueue("Narrative", incident.Id, "{\"text\":\"encrypted\"}");
    var encryptedBytes = File.ReadAllBytes(tempQueuePath);
    Check(!System.Text.Encoding.UTF8.GetString(encryptedBytes).Contains("encrypted", StringComparison.Ordinal), "mobile offline cache encryption");
    var reopenedQueue = new OfflineOperationStore(tempQueuePath, protector);
    Check(reopenedQueue.Items.Count == 1, "mobile encrypted cache reload");
    Check(reopenedQueue.Remove(encryptedQueuedItem.OperationId) && reopenedQueue.Items.Count == 0, "mobile rejected/conflicting queued work can be explicitly discarded without an authoritative write");
}
File.Delete(tempQueuePath);
// Advanced CAD and back-office depth
incident = core.GetIncident(incident.Id);
var additionalAlarm = features.CommitAdditionalAlarm(incident.Id, new CommitAdditionalAlarmRequest(incident.Version, [unitIds[0]], ["StationDisplay", "RunSheet"], "selftest", "selftest:alarm2"));
Check(additionalAlarm.AlarmNumber == 2, "additional alarm dispatch");
var cancelResult = features.CancelDispatch(additionalAlarm.DispatchActionId, new CancelDispatchRequest("Self-test cancellation", "selftest", "selftest:cancel"));
Check(cancelResult.State == "Cancelled", "dispatch cancellation");
var avl = features.ObserveAvl(new UpsertAvlObservationRequest(agency, unitIds[0], 40.0, -74.0, 15, 100, "SelfTestAVL", DateTimeOffset.UtcNow));
Check(avl.Observation.UnitId == unitIds[0], "AVL observation");
if (avl.Advisory is not null)
{
    features.ResolveAvlAdvisory(avl.Advisory.Id, new ResolveAvlAdvisoryRequest("selftest", true, "selftest:avl:resolve"));
    Check(features.AvlAdvisories.First(x => x.Id == avl.Advisory.Id).State == "Applied", "AVL advisory resolution");
}
incident = core.GetIncident(incident.Id);
var beforeCorrectionUtc = DateTimeOffset.UtcNow;
var correction = features.CorrectIncident(incident.Id, new CorrectIncidentFieldRequest(incident.Version, "Address", "500 Test Avenue Rear", "Caller clarification", "selftest", "selftest:correction"));
Check(correction.OriginalValue != correction.CorrectedValue, "auditable incident correction");
var replayService = new ReplayService(core, features);
var restrictionReplay = replayService.GetIncidentSnapshot(incident.Id, restrictionActiveAtUtc);
Check(restrictionReplay.RoadRestrictions.Any(x => x.Id == restriction.Id && x.Active), "historical replay preserves road restriction active state before later end");
var historicalReplay = replayService.GetIncidentSnapshot(incident.Id, beforeCorrectionUtc);
Check(historicalReplay.Incident.Address == "500 Test Avenue", "historical replay reconstructs pre-correction incident state");
Check(historicalReplay.CorrectedLater.Any(x => x.Id == correction.Id), "historical replay identifies corrections made later");
Check(historicalReplay.Timeline.Any(x => x.Classification == "CORRECTED LATER"), "historical replay classifies later corrections");
Check(historicalReplay.Units.Any(x => x.IncidentId == incident.Id), "historical replay reconstructs assigned units");
Check(historicalReplay.DispatchActions.Count > 0, "historical replay reconstructs dispatch state");
var currentReplay = replayService.GetIncidentSnapshot(incident.Id);
Check(currentReplay.Incident.Address == "500 Test Avenue Rear", "current replay reflects corrected incident state");

var rotation = features.UpsertRotation(null, new UpsertRotationRequest(agency, "24/72 Test", "24-on/72-off", DateOnly.FromDateTime(DateTime.Today), true, "selftest"));
Check(rotation.Enabled, "workforce rotation");
var overtime = features.CreateOvertimeOffer(new CreateOvertimeOfferRequest(agency, DateOnly.FromDateTime(DateTime.Today.AddDays(1)), "Day", station.Id, "Driver", "Driver", 1), "selftest");
overtime = features.AcceptOvertime(overtime.Id, new AcceptOvertimeRequest("Employee"));
Check(overtime.State == "Filled", "overtime fill workflow");
var timesheet = features.UpsertTimesheet(null, new UpsertTimesheetEntryRequest(agency, "Employee", DateOnly.FromDateTime(DateTime.Today), 8m, "Regular", station.Id, incident.Id, "Self test"), "selftest");
timesheet = features.ApproveTimesheet(timesheet.Id, new ApproveTimesheetRequest("Supervisor", true));
Check(timesheet.Status == "Approved", "timesheet approval");
var payroll = features.PayrollExport(agency, DateOnly.FromDateTime(DateTime.Today.AddDays(-1)), DateOnly.FromDateTime(DateTime.Today.AddDays(1)));
Check(payroll.Lines.Any(x => x.EmployeeName == "Employee" && x.Hours == 8m), "payroll export");
Check(features.DailyRoster(agency, DateOnly.FromDateTime(DateTime.Today)).AgencyId == agency, "daily roster");


var workforceDate = DateOnly.FromDateTime(DateTime.Today);
var rosterDriverQualification = features.UpsertQualification(null, new UpsertPersonnelQualificationRequest(agency, "EMP-DRIVER", "Roster Driver", "Driver", workforceDate.AddDays(-30), null, "Active", "SelfTest"), "selftest");
var rosterOfficerQualification = features.UpsertQualification(null, new UpsertPersonnelQualificationRequest(agency, "EMP-OFFICER", "Roster Officer", "Officer", workforceDate.AddDays(-30), null, "Active", "SelfTest"), "selftest");
Check(rosterDriverQualification.State == "Active" && rosterOfficerQualification.State == "Active", "workforce qualification prerequisites");
var staffingDriverRule = features.UpsertStaffingRule(null, new UpsertStaffingRuleRequest(agency, station.Id, "Day", "Driver", 1, "Driver", true), "workforce-supervisor");
var staffingOfficerRule = features.UpsertStaffingRule(null, new UpsertStaffingRuleRequest(agency, station.Id, "Day", "Officer", 1, "Officer", true), "workforce-supervisor");
var crossAgencyRuleRejected = false;
try { features.UpsertStaffingRule(null, new UpsertStaffingRuleRequest(Guid.NewGuid(), station.Id, "Day", "Officer", 1, "Officer", true), "workforce-supervisor"); }
catch (InvalidOperationException) { crossAgencyRuleRejected = true; }
Check(crossAgencyRuleRejected, "workforce staffing rule rejects cross-agency station");
var workforceUnit = core.Units.First(x => x.StationId == station.Id);
var driverAssignment = features.UpsertWorkforceAssignment(null, new UpsertWorkforceAssignmentRequest(agency, workforceDate, "Day", station.Id, workforceUnit.Id, "Driver", "EMP-DRIVER", "Roster Driver", "Driver", "Regular", "Assigned", null, null, "MasterSchedule"), "workforce-supervisor");
var officerAssignment = features.UpsertWorkforceAssignment(null, new UpsertWorkforceAssignmentRequest(agency, workforceDate, "Day", station.Id, workforceUnit.Id, "Officer", "EMP-OFFICER", "Roster Officer", "Officer", "Regular", "Assigned", null, null, "MasterSchedule"), "workforce-supervisor");
var workforceStaffing = features.EvaluateStaffing(agency, workforceDate);
Check(workforceStaffing.MeetsMinimums && workforceStaffing.RequiredPositions == 2 && workforceStaffing.FilledPositions == 2, "qualification-aware workforce minimum staffing met");
var postedRoster1 = features.PublishWorkforceRoster(agency, workforceDate, new PublishWorkforceRosterRequest("Initial posted roster"), "workforce-supervisor");
Check(postedRoster1.Revision == 1 && postedRoster1.State == "Posted" && postedRoster1.Assignments.Count == 2 && postedRoster1.ContentSha256.Length == 64, "immutable workforce roster publication");
features.CallOutWorkforceAssignment(driverAssignment.Id, new WorkforceActionRequest("Sick call"), "workforce-supervisor");
workforceStaffing = features.EvaluateStaffing(agency, workforceDate);
Check(!workforceStaffing.MeetsMinimums && workforceStaffing.Vacancies == 1, "call-out creates visible staffing deficiency without auto-assignment");
var openDriverAssignment = features.WorkforceAssignments.Single(x => x.Date == workforceDate && x.Position == "Driver" && x.State == "Open");
Check(string.IsNullOrWhiteSpace(openDriverAssignment.EmployeeName), "call-out vacancy remains explicitly open");
var reliefAssignment = features.RelieveWorkforceAssignment(officerAssignment.Id, new WorkforceReliefRequest("EMP-OFFICER-2", "Relief Officer", "Officer", "Relief", "Mid-shift relief"), "workforce-supervisor");
Check(reliefAssignment.State == "Assigned" && features.WorkforceAssignments.Single(x => x.Id == officerAssignment.Id).State == "Relieved", "atomic workforce relief replaces active assignment");
var filledDriverAssignment = features.UpsertWorkforceAssignment(openDriverAssignment.Id, new UpsertWorkforceAssignmentRequest(agency, workforceDate, "Day", station.Id, workforceUnit.Id, "Driver", "EMP-DRIVER-2", "Overtime Driver", "Driver", "Overtime", "Overtime", openDriverAssignment.EffectiveAtUtc, null, "OvertimeCoverage"), "workforce-supervisor");
Check(filledDriverAssignment.State == "Overtime", "open workforce vacancy can be filled explicitly");
workforceStaffing = features.EvaluateStaffing(agency, workforceDate);
Check(workforceStaffing.MeetsMinimums && workforceStaffing.Vacancies == 0, "workforce staffing returns to minimum after explicit coverage");
var postedRoster2 = features.PublishWorkforceRoster(agency, workforceDate, new PublishWorkforceRosterRequest("Coverage posted"), "workforce-supervisor");
Check(postedRoster2.Revision == 2 && postedRoster2.State == "Posted", "workforce roster revision increments without overwriting prior post");
Check(features.WorkforceRosterSnapshots.Single(x => x.Id == postedRoster1.Id).Assignments.Any(x => x.EmployeeName == "Roster Driver"), "original posted workforce roster remains immutable after live changes");
var rosterView = features.GetWorkforceRoster(agency, workforceDate);
Check(rosterView.PostedRoster?.Id == postedRoster2.Id && rosterView.Changes.Any(x => x.Kind == "CallOut") && rosterView.Changes.Any(x => x.Kind == "Relief"), "current workforce roster includes latest post and structured change history");
var rosterPrint = features.BuildWorkforceRosterPrint(agency, workforceDate, includeChanges: true);
Check(rosterPrint.Content.Contains("SAME-DAY CHANGES", StringComparison.Ordinal) && rosterPrint.Content.Contains("Overtime Driver", StringComparison.Ordinal) && rosterPrint.ContentSha256.Length == 64, "workforce roster print view includes current schedule and changes");

var workflow = features.CreateApprovalWorkflow(new CreateApprovalWorkflowRequest(agency, "Document", document.Id, "Policy Approval", "Sequential", [new ApprovalStepDefinition(1, "Supervisor", true), new ApprovalStepDefinition(2, "Chief", true)]), "selftest");
var firstStep = features.ApprovalSteps.Single(x => x.WorkflowId == workflow.Id && x.Sequence == 1);
features.DecideApprovalStep(firstStep.Id, new DecideApprovalStepRequest("Supervisor", "Approve", "Looks good"));
var secondStep = features.ApprovalSteps.Single(x => x.WorkflowId == workflow.Id && x.Sequence == 2);
Check(secondStep.State == "Pending", "sequential approval advancement");
features.DecideApprovalStep(secondStep.Id, new DecideApprovalStepRequest("Chief", "Approve", null));
Check(features.ApprovalWorkflows.Single(x => x.Id == workflow.Id).State == "Approved", "approval workflow completion");

var violation = features.CreateViolation(new CreateViolationRequest(agency, premise.Id, inspection.Id, "TEST-1", "Self-test violation", "High", DateOnly.FromDateTime(DateTime.Today), DateOnly.FromDateTime(DateTime.Today.AddDays(30))), "selftest");
violation = features.ResolveViolation(violation.Id, "selftest");
Check(violation.State == "Resolved", "violation lifecycle");
var hazmat = features.UpsertHazmat(null, new UpsertHazmatRecordRequest(agency, premise.Id, "Propane", "UN1978", "2 cylinders", "Rear cage", "Flammable gas", "Active"), "selftest");
Check(hazmat.Material == "Propane", "hazmat record");
var investigation = features.CreateInvestigation(new CreateInvestigationRequest(agency, incident.Id, premise.Id, "Self-test Investigation", "Investigator", "Test summary"), "selftest");
investigation = features.CloseInvestigation(investigation.Id, "selftest");
Check(investigation.State == "Closed", "investigation lifecycle");
var qualification = features.UpsertQualification(null, new UpsertPersonnelQualificationRequest(agency, "EMP-1", "Employee", "Driver", DateOnly.FromDateTime(DateTime.Today), null, "Active", "SelfTest"), "selftest");
Check(qualification.State == "Active", "personnel qualification");

var shared = features.GetCollaborativeNarrative(incident.Id);
shared = features.ReplaceCollaborativeNarrative(incident.Id, new ReplaceCollaborativeNarrativeRequest(shared.Version, "Initial command narrative", "dispatcher-a", "selftest:shared:1"));
shared = features.PatchCollaborativeNarrative(incident.Id, new CollaborativeNarrativePatchRequest(shared.Version, shared.Text.Length, 0, "\nSecond dispatcher update", "dispatcher-b", "selftest:shared:2"));
Check(shared.Version == 2 && shared.Text.Contains("Second dispatcher update", StringComparison.Ordinal), "collaborative narrative versioning");
var concurrentBaseVersion = shared.Version;
var concurrentBaseLength = shared.Text.Length;
shared = features.PatchCollaborativeNarrative(incident.Id, new CollaborativeNarrativePatchRequest(concurrentBaseVersion, 0, 0, "[A] ", "dispatcher-a", "selftest:shared:ot:a"));
shared = features.PatchCollaborativeNarrative(incident.Id, new CollaborativeNarrativePatchRequest(concurrentBaseVersion, concurrentBaseLength, 0, " [B]", "dispatcher-b", "selftest:shared:ot:b"));
var narrativeOps = features.GetCollaborativeNarrativeOperations(incident.Id);
Check(shared.Version == 4 && shared.Text.StartsWith("[A] ", StringComparison.Ordinal) && shared.Text.EndsWith(" [B]", StringComparison.Ordinal), "collaborative narrative non-overlap rebase");
Check(narrativeOps.Any(x => x.Rebased && x.ClientBaseVersion == concurrentBaseVersion && x.ResultVersion == 4), "collaborative narrative operation history");

var operationalMessage = features.SendOperationalMessage(new CreateOperationalMessageRequest(agency, incident.Id, "Command", "dispatcher-a", "supervisor-a", "Urgent", "Operational", "Evacuate Division 2", "{}", ["supervisor-a"]), "selftest");
var queuedReceipt = features.GetQueuedReceipts("supervisor-a", null).Single(x => x.MessageId == operationalMessage.Id);
features.UpdateMessageReceipt(queuedReceipt.Id, new UpdateMessageReceiptRequest("supervisor-a", "selftest-device", "Delivered"), "local-development");
features.UpdateMessageReceipt(queuedReceipt.Id, new UpdateMessageReceiptRequest("supervisor-a", "selftest-device", "Acknowledged"), "local-development");
var promoted = features.PromoteMessageToNarrative(operationalMessage.Id, new PromoteMessageToNarrativeRequest("selftest", "Promoted command message", "selftest:promote-message"));
Check(promoted.Message.Contains("Evacuate Division 2", StringComparison.Ordinal), "operational message promotion to narrative");
Check(features.OperationalMessages.Single(x => x.Id == operationalMessage.Id).State == "Acknowledged", "message delivery/read/ack state");

// Pass 22 / 22.1: full internal chat plus identity/membership/realtime hardening.
var chatIdentityState = features.ExportState();
var provisionedChatUsers = chatIdentityState.Users.Concat([
    new UserSecurityDto("dispatcher-a", "Dispatcher A", agency, ["Dispatcher"], false, false, false, "Active", DateTimeOffset.UtcNow),
    new UserSecurityDto("supervisor-a", "Supervisor A", agency, ["Supervisor"], false, false, false, "Active", DateTimeOffset.UtcNow),
    new UserSecurityDto("agency-viewer", "Agency Viewer", agency, ["Dispatcher"], false, false, false, "Active", DateTimeOffset.UtcNow),
    new UserSecurityDto("station-user", "Station User", agency, ["Station"], false, false, false, "Active", DateTimeOffset.UtcNow),
    new UserSecurityDto("group-user", "Group User", agency, ["Dispatcher"], false, false, false, "Active", DateTimeOffset.UtcNow),
    new UserSecurityDto("mobile-user", "Mobile User", agency, ["Mobile", "Fire"], false, false, false, "Active", DateTimeOffset.UtcNow)
]).GroupBy(x => x.UserId, StringComparer.OrdinalIgnoreCase).Select(x => x.Last()).ToArray();
features.ImportState(chatIdentityState with { Users = provisionedChatUsers });
Check(features.ResolveActorAgency("dispatcher-a") == agency, "chat actor agency resolves from provisioned DISRoute identity");
var unknownActorRejected = false;
try { features.ResolveActorAgency("unlisted-user"); } catch (UnauthorizedAccessException) { unknownActorRejected = true; }
Check(unknownActorRejected && !features.CanAccessChatChannel(Guid.NewGuid(), "unlisted-user"), "unprovisioned chat actors fail closed");

var stationMembershipAssignment = features.UpsertWorkforceAssignment(null, new UpsertWorkforceAssignmentRequest(agency, workforceDate, "Day", station.Id, workforceUnit.Id, "ChatObserver", "station-user", "Station User", "", "Regular", "Assigned", null, null, "ChatMembershipSelfTest"), "workforce-supervisor");
var stationChat = features.CreateChatChannel(new CreateChatChannelRequest(agency, "Station", "Station Operations", "Dynamic station membership", station.Id, null, null, null, null), "dispatcher-a");
Check(features.CanAccessChatChannel(stationChat.Id, "station-user"), "station chat membership resolves from effective workforce station assignment");
features.CallOutWorkforceAssignment(stationMembershipAssignment.Id, new WorkforceActionRequest("Chat membership call-out"), "workforce-supervisor");
Check(!features.CanAccessChatChannel(stationChat.Id, "station-user"), "station chat access is removed when effective workforce membership ends");

var groupWithMembers = features.SetOperationalGroupMembers(group.Id, new UpdateOperationalGroupMembersRequest(["group-user"]), "local-development");
Check((groupWithMembers.MemberUserIds ?? []).Contains("group-user"), "operational group stores explicit user membership");
var groupChat = features.CreateChatChannel(new CreateChatChannelRequest(agency, "Group", "Division 2 Chat", "Dynamic operational-group membership", null, incident.Id, null, null, null, group.Id), "dispatcher-a");
Check(groupChat.OperationalGroupId == group.Id && features.CanAccessChatChannel(groupChat.Id, "group-user"), "group chat resolves access from bound operational group membership");
features.SetOperationalGroupMembers(group.Id, new UpdateOperationalGroupMembersRequest([]), "local-development");
Check(!features.CanAccessChatChannel(groupChat.Id, "group-user"), "group chat access changes with operational group membership");

var directChat = features.CreateChatChannel(new CreateChatChannelRequest(agency, "Direct", "Dispatcher / Supervisor", "Direct operational coordination", null, null, null, ["dispatcher-a", "supervisor-a"], null), "dispatcher-a");
Check(directChat.Type == "Direct" && directChat.MemberUserIds.Count == 2, "direct chat requires exactly two explicit provisioned members");
var chatMessage = features.SendChatMessage(directChat.Id, new SendChatMessageRequest("@supervisor-a review the evacuation group", "Important", "Operational", incident.Id, ["supervisor-a"], [new ChatAttachmentReferenceDto(Guid.Empty, "evacuation-map.pdf", "application/pdf", "dms://selftest/evacuation-map", new string('a', 64), "Internal")]), "dispatcher-a");
Check(chatMessage.Sender == "dispatcher-a" && chatMessage.LinkedIncidentId == incident.Id && chatMessage.Mentions.Contains("supervisor-a") && chatMessage.Attachments.Single().Id != Guid.Empty, "chat message mention, incident link and attachment reference");
var badAttachmentHashRejected = false;
try { features.SendChatMessage(directChat.Id, new SendChatMessageRequest("bad attachment", "Normal", "Internal", null, null, [new ChatAttachmentReferenceDto(Guid.Empty, "bad.bin", "application/octet-stream", "dms://bad", "not-a-sha", "Internal")]), "dispatcher-a"); } catch (ArgumentException) { badAttachmentHashRejected = true; }
Check(badAttachmentHashRejected, "chat attachment SHA-256 metadata is validated when supplied");
var supervisorChannels = features.GetChatChannels(agency, "supervisor-a", false);
Check(supervisorChannels.Single(x => x.Channel.Id == directChat.Id).UnreadCount == 1, "chat unread count is recipient-specific");
var chatRead = features.MarkChatRead(directChat.Id, chatMessage.Id, "supervisor-a");
Check(chatRead.UnreadCount == 0 && features.GetChatChannels(agency, "supervisor-a", false).Single(x => x.Channel.Id == directChat.Id).UnreadCount == 0, "chat read cursor clears unread state");
Check(features.SearchChat(agency, "supervisor-a", "evacuation", 20).Any(x => x.Message.Id == chatMessage.Id), "chat search is scoped to accessible history");
var outsiderDenied = false;
try { features.GetChatHistory(directChat.Id, "unlisted-user", null, 20); } catch (UnauthorizedAccessException) { outsiderDenied = true; }
Check(outsiderDenied, "direct chat denies unprovisioned users");
var agencyChat = features.CreateChatChannel(new CreateChatChannelRequest(agency, "Agency", "Agency Operations", "Agency-wide coordination", null, null, null, null, null), "local-development");
Check(features.GetChatChannels(agency, "agency-viewer", false).Any(x => x.Channel.Id == agencyChat.Id), "agency chat is visible to provisioned users in the owner agency");
var crossAgencyQueryRejected = false;
try { features.GetChatChannels(Guid.NewGuid(), "agency-viewer", false); } catch (UnauthorizedAccessException) { crossAgencyQueryRejected = true; }
Check(crossAgencyQueryRejected, "client-supplied agency cannot widen chat query scope");
var partnerAgency = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
var stateWithPartner = features.ExportState();
features.ImportState(stateWithPartner with { Users = stateWithPartner.Users.Append(new UserSecurityDto("partner-dispatcher", "Partner Dispatcher", partnerAgency, ["Dispatcher"], false, false, false, "Active", DateTimeOffset.UtcNow)).ToArray() });
var mutualAidChat = features.CreateChatChannel(new CreateChatChannelRequest(agency, "MutualAid", "Mutual Aid Command", "Explicit interagency room", null, incident.Id, [partnerAgency], ["dispatcher-a", "partner-dispatcher"], null), "dispatcher-a");
Check(mutualAidChat.ParticipatingAgencyIds.Contains(partnerAgency) && features.CanAccessChatChannel(mutualAidChat.Id, "partner-dispatcher"), "mutual-aid chat requires explicit participating agency and member access");
var crossAgencyOutsiderDenied = !features.CanAccessChatChannel(mutualAidChat.Id, "agency-viewer");
Check(crossAgencyOutsiderDenied, "mutual-aid chat is not exposed to owner-agency nonmembers");
var mutualMessage = features.SendChatMessage(mutualAidChat.Id, new SendChatMessageRequest("Partner command check-in", "Normal", "Operational", incident.Id, null, null), "partner-dispatcher");
Check(mutualMessage.OwnerAgencyId == agency, "mutual-aid message preserves owning channel agency");
features.SetChatChannelArchived(directChat.Id, true, "dispatcher-a");
var archivedWriteRejected = false;
try { features.SendChatMessage(directChat.Id, new SendChatMessageRequest("should fail", "Normal", "Operational", null, null, null), "dispatcher-a"); } catch (InvalidOperationException) { archivedWriteRejected = true; }
Check(archivedWriteRejected, "archived chat channels are read-only");

// Pass 23: assignment-scoped field package plus explicit offline reconciliation safety.
var mobileDevice = features.RegisterDevice(new RegisterDeviceRequest(agency, "mobile-selftest", "Self Test Mobile", "Mobile", "Android", null), "selftest");
var mobileAssignment = features.UpsertWorkforceAssignment(null, new UpsertWorkforceAssignmentRequest(agency, workforceDate, "Day", station.Id, unitIds[0], "Firefighter", "mobile-user", "Mobile User", "Firefighter", "Regular", "Assigned", DateTimeOffset.UtcNow.AddMinutes(-5), null, "MobileSelfTest"), "workforce-supervisor");
var mobileRadio = features.UpsertRadioStream(null, new UpsertRadioStreamRequest(agency, "Fireground Monitor", "https://radio.example.test/fireground.opus", "Opus", "Mobile"), "selftest");
var initialMobileSync = features.MobileSync(new MobileSyncRequest(agency, mobileDevice.DeviceId, null, [], unitIds[0]), "mobile-user");
var initialMobilePackage = initialMobileSync.OperationalPackage ?? throw new InvalidOperationException("Mobile operational package missing.");
Check(initialMobileSync.Units.Count == 1 && initialMobilePackage.SelectedUnitId == unitIds[0] && initialMobilePackage.CurrentIncident?.Id == incident.Id, "mobile sync is scoped to the signed-in user's effective unit/incident assignment");
Check(initialMobilePackage.Crew.Any(x => x.Id == mobileAssignment.Id), "mobile package includes current unit crew accountability");
Check(initialMobilePackage.SiteIntelligence?.PublishedPreplans.Any(x => x.PremiseId == premise.Id) == true && initialMobilePackage.SiteIntelligence.CrgAssets.Count > 0, "mobile package carries published preplan and CRG scene intelligence");
Check(initialMobilePackage.Maydays.Any(x => x.Id == mayday.Id && x.State != "Resolved"), "mobile package carries active Mayday state");
Check(initialMobilePackage.Documents.Any(x => x.DocumentId == dmsDocument.Id && x.Version == dmsRevision2.Version), "mobile package carries current published incident-linked DMS document");
Check(initialMobilePackage.RadioStreams.Any(x => x.Id == mobileRadio.Id && x.Codec == "Opus"), "mobile package exposes only authorized listen-only radio streams");
Check(initialMobilePackage.ChatChannels.Any(x => x.Channel.Id == agencyChat.Id), "mobile package carries accessible internal chat channels");

var mobileUnitBeforeOffline = core.Units.Single(x => x.Id == unitIds[0]);
var mobileTargetStatus = mobileUnitBeforeOffline.Status == "OnScene" ? "EnRoute" : "OnScene";
var mobileNarrativeOperation = new QueuedMobileOperationDto(Guid.NewGuid(), "Narrative", incident.Id, System.Text.Json.JsonSerializer.Serialize(new MobileNarrativePayload("Pass 23 offline narrative")), DateTimeOffset.UtcNow.AddSeconds(-3));
var mobileMessageOperation = new QueuedMobileOperationDto(Guid.NewGuid(), "Message", incident.Id, System.Text.Json.JsonSerializer.Serialize(new MobileMessagePayload("Mobile", "Pass 23 offline operational message")), DateTimeOffset.UtcNow.AddSeconds(-2));
var mobileChatOperation = new QueuedMobileOperationDto(Guid.NewGuid(), "ChatMessage", incident.Id, System.Text.Json.JsonSerializer.Serialize(new MobileChatPayload(agencyChat.Id, "Pass 23 offline chat message")), DateTimeOffset.UtcNow.AddSeconds(-1));
var mobileStatusOperation = new QueuedMobileOperationDto(Guid.NewGuid(), "UnitStatus", incident.Id, System.Text.Json.JsonSerializer.Serialize(new MobileUnitStatusPayload(unitIds[0], mobileUnitBeforeOffline.Status, mobileTargetStatus, mobileUnitBeforeOffline.UpdatedAtUtc)), DateTimeOffset.UtcNow);
var mobileLease = new OfflineAuthorizationLeaseValidationDto(true, "mobile-user", agency, ["unit.change-status"], DateTimeOffset.UtcNow.AddMinutes(30), null);
var reconciledMobileSync = features.MobileSync(new MobileSyncRequest(agency, mobileDevice.DeviceId, null, [mobileNarrativeOperation, mobileMessageOperation, mobileChatOperation, mobileStatusOperation], unitIds[0], "selftest-mobile-lease"), "mobile-user", mobileLease);
Check(reconciledMobileSync.OperationResults.All(x => x.State == "Accepted"), "approved offline mobile narrative/message/chat/status operations reconcile explicitly");
Check(core.GetActivity(incident.Id).Any(x => x.Message.Contains("Pass 23 offline narrative", StringComparison.Ordinal)), "offline mobile narrative is committed to authoritative incident activity");
Check(core.Messages.Any(x => x.IncidentId == incident.Id && x.Text == "Pass 23 offline operational message"), "offline mobile operational message is actually committed");
Check(features.GetChatHistory(agencyChat.Id, "mobile-user", null, 50).Any(x => x.Id == mobileChatOperation.OperationId || x.Body == "Pass 23 offline chat message"), "offline mobile chat message is actually committed");
Check(core.Units.Single(x => x.Id == unitIds[0]).Status == mobileTargetStatus, "leased offline mobile unit-status change is committed after stale-state check");
var duplicateMobileSync = features.MobileSync(new MobileSyncRequest(agency, mobileDevice.DeviceId, null, [mobileNarrativeOperation, mobileMessageOperation, mobileChatOperation, mobileStatusOperation], unitIds[0], "selftest-mobile-lease"), "mobile-user", mobileLease);
Check(duplicateMobileSync.OperationResults.All(x => x.State == "DuplicateIgnored"), "mobile offline reconciliation is idempotent across all approved write kinds");

var staleMobileStatusOperation = new QueuedMobileOperationDto(Guid.NewGuid(), "UnitStatus", incident.Id, System.Text.Json.JsonSerializer.Serialize(new MobileUnitStatusPayload(unitIds[0], mobileUnitBeforeOffline.Status, "Available", mobileUnitBeforeOffline.UpdatedAtUtc)), DateTimeOffset.UtcNow);
var staleMobileSync = features.MobileSync(new MobileSyncRequest(agency, mobileDevice.DeviceId, null, [staleMobileStatusOperation], unitIds[0], "selftest-mobile-lease"), "mobile-user", mobileLease);
Check(staleMobileSync.OperationResults.Single().State == "Conflict" && core.Units.Single(x => x.Id == unitIds[0]).Status == mobileTargetStatus, "stale offline unit status is rejected instead of overwriting authoritative state");
var noLeaseStatusOperation = new QueuedMobileOperationDto(Guid.NewGuid(), "UnitStatus", incident.Id, System.Text.Json.JsonSerializer.Serialize(new MobileUnitStatusPayload(unitIds[0], mobileTargetStatus, "Available", core.Units.Single(x => x.Id == unitIds[0]).UpdatedAtUtc)), DateTimeOffset.UtcNow);
var noLeaseMobileSync = features.MobileSync(new MobileSyncRequest(agency, mobileDevice.DeviceId, null, [noLeaseStatusOperation], unitIds[0]), "mobile-user");
Check(noLeaseMobileSync.OperationResults.Single().State == "Rejected" && core.Units.Single(x => x.Id == unitIds[0]).Status == mobileTargetStatus, "offline unit status cannot reconcile without a valid authorization lease");
var unauthorizedMobileUnitRejected = false;
try { features.MobileSync(new MobileSyncRequest(agency, mobileDevice.DeviceId, null, [], unitIds[1]), "mobile-user"); } catch (UnauthorizedAccessException) { unauthorizedMobileUnitRejected = true; }
Check(unauthorizedMobileUnitRejected, "mobile user cannot request another unit outside current workforce assignment");
var stationDeviceRejectedForMobile = false;
try { features.MobileSync(new MobileSyncRequest(agency, device.DeviceId, null, [], unitIds[0]), "mobile-user"); } catch (UnauthorizedAccessException) { stationDeviceRejectedForMobile = true; }
Check(stationDeviceRejectedForMobile, "non-Mobile/MDT registered device cannot use mobile operational sync");

var tempCachePath = Path.Combine(Path.GetTempPath(), "disroute-mobile-cache-selftest-" + Guid.NewGuid().ToString("N") + ".bin");
using (var cacheProtector = new AesGcmOfflineDataProtector(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)))
{
    var cacheStore = new MobileCacheStore(tempCachePath, cacheProtector);
    cacheStore.Save(reconciledMobileSync);
    var encryptedCacheBytes = File.ReadAllBytes(tempCachePath);
    Check(!System.Text.Encoding.UTF8.GetString(encryptedCacheBytes).Contains(incident.Number, StringComparison.Ordinal), "mobile operational snapshot cache is encrypted at rest");
    var restoredCache = cacheStore.Load();
    Check(restoredCache?.Response.OperationalPackage?.CurrentIncident?.Id == incident.Id, "encrypted mobile operational snapshot restores for stale/offline read-only use");
}
File.Delete(tempCachePath);

var progressiveAlert = features.CreateAlert(new CreateAlertRequest(agency, incident.Id, "SelfTest", "Info", "selftest:progressive", "test", "No response yet"), "selftest");
var escalated = features.EscalateUnacknowledgedAlerts(progressiveAlert.CreatedAtUtc.AddMinutes(10), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2));
Check(escalated.Any(x => x.Id == progressiveAlert.Id && x.Severity == "Warning"), "progressive alert escalation");
var acknowledgedAlert = features.ActOnAlert(progressiveAlert.Id, "acknowledge", new AlertActionRequest("selftest-dispatcher", "Seen from CAD"));
Check(acknowledgedAlert.State == "Acknowledged", "dispatcher alert acknowledgement");
var claimedAlert = features.ActOnAlert(progressiveAlert.Id, "claim", new AlertActionRequest("selftest-dispatcher", "Taking ownership"));
Check(claimedAlert.State == "Claimed" && claimedAlert.ClaimedBy == "selftest-dispatcher", "dispatcher alert claim");
var resolvedAlert = features.ActOnAlert(progressiveAlert.Id, "resolve", new AlertActionRequest("selftest-dispatcher", "Resolved"));
Check(resolvedAlert.State == "Resolved", "dispatcher alert resolution");


// Pass 18: canonical CAD-to-RMS handoff, records provenance, review/finalization and amendment safety.
var rmsCadIncident = core.CreateIncident(new CreateIncidentRequest(agency, station.Id, "Warehouse Fire", "Emergency", "700 Records Avenue", "Archive / Ledger", "Test City", "Records Caller", "555-0188", "RMS handoff probe", "selftest:rms-cad-create"), "selftest");
var rmsUnit = core.Units.First(x => x.IncidentId is null);
rmsCadIncident = core.AssignUnit(rmsCadIncident.Id, new AssignUnitRequest(rmsUnit.Id, rmsCadIncident.Version, true, "selftest:rms-assign"), "selftest");
features.SetCommand(new SetCommandRequest(agency, rmsCadIncident.Id, "Incident Command", rmsUnit.Id, "Records IC", "selftest"));
var rmsMutualAid = features.CreateMutualAid(new CreateMutualAidRequest(agency, rmsCadIncident.Id, "Neighbor FD", "Engine", 1, "selftest"));
features.UpdateMutualAid(rmsMutualAid.Id, "Accepted", "selftest");
core.AddNarrative(rmsCadIncident.Id, new AddNarrativeRequest("Initial warehouse fire narrative", "selftest:rms-narrative"), "selftest");
var rmsShared = features.GetCollaborativeNarrative(rmsCadIncident.Id);
features.ReplaceCollaborativeNarrative(rmsCadIncident.Id, new ReplaceCollaborativeNarrativeRequest(rmsShared.Version, "Shared warehouse fire narrative", "records-dispatcher", "selftest:rms-shared"));
var rmsReporting = new RmsReportingService(core, features);
var rmsReport = rmsReporting.CreateFromIncident(rmsCadIncident.Id, new CreateRmsFromIncidentRequest(agency, "FireIncident", "Initial records summary", "selftest:rms-create"), "records-user");
Check(rmsReport.IncidentId == rmsCadIncident.Id && rmsReport.Status == "Draft" && rmsReport.Version == 1, "RMS CAD handoff identity");
Check(rmsReport.Units.Any(x => x.UnitId == rmsUnit.Id), "RMS CAD handoff responding unit snapshot");
Check(rmsReport.CommandAssignments.Any(x => x.CommandName == "Incident Command"), "RMS CAD handoff command snapshot");
Check(rmsReport.MutualAid.Any(x => x.RequestedAgency == "Neighbor FD" && x.State == "Accepted"), "RMS CAD handoff mutual aid snapshot");
Check(rmsReport.Provenance.Any(x => x.Field == "Address" && x.SourceKind == "CAD"), "RMS CAD provenance capture");
Check(!core.ValidateRmsIncidentReport(rmsReport.Id).CanonicalReady, "open CAD incident prevents RMS canonical readiness");

rmsReport = core.UpdateRmsIncidentReport(rmsReport.Id, new UpdateRmsIncidentReportRequest(rmsReport.Version, null, null, "700 Records Ave — Records Corrected", null, null, null, null, null, "Records-authored summary", "selftest:rms-edit"), "records-user");
rmsCadIncident = core.UpdateIncident(rmsCadIncident.Id, new UpdateIncidentRequest(rmsCadIncident.Version, "Commercial Structure Fire", rmsCadIncident.Priority, "700 CAD Corrected Avenue", rmsCadIncident.CrossStreets, rmsCadIncident.Municipality, rmsCadIncident.CallerName, rmsCadIncident.CallerPhone, rmsCadIncident.Notes, "selftest:rms-cad-update"), "dispatcher");
rmsReport = rmsReporting.RefreshFromCad(rmsReport.Id, new RefreshRmsCadSnapshotRequest(rmsReport.Version, "selftest:rms-refresh-1"), "records-user");
Check(rmsReport.Nature == "Commercial Structure Fire", "CAD-owned RMS field refresh");
Check(rmsReport.Address == "700 Records Ave — Records Corrected", "records override survives CAD refresh");
Check(rmsReport.Provenance.Any(x => x.Field == "Address" && x.SourceKind == "RecordsUser"), "records override provenance");

rmsCadIncident = core.CloseIncident(rmsCadIncident.Id, new CloseIncidentRequest(rmsCadIncident.Version, "Fire controlled", "selftest:rms-close"), "dispatcher");
rmsReport = rmsReporting.RefreshFromCad(rmsReport.Id, new RefreshRmsCadSnapshotRequest(rmsReport.Version, "selftest:rms-refresh-closed"), "records-user");
var rmsValidation = core.ValidateRmsIncidentReport(rmsReport.Id);
Check(rmsReport.ClosedAtUtc is not null && rmsValidation.CanonicalReady && rmsValidation.ProviderValidationPending, "closed RMS report canonical readiness and provider-validation boundary");
Check(rmsReport.Units.Any(x => x.UnitId == rmsUnit.Id), "RMS unit snapshot survives CAD unit release");

rmsReport = core.SubmitRmsForReview(rmsReport.Id, new SubmitRmsForReviewRequest(rmsReport.Version, "Ready for supervisor review", "selftest:rms-submit-1"), "records-user");
rmsReport = core.ReviewRmsIncidentReport(rmsReport.Id, new ReviewRmsIncidentReportRequest(rmsReport.Version, "return", "Clarify summary", "selftest:rms-return"), "records-supervisor");
rmsReport = core.UpdateRmsIncidentReport(rmsReport.Id, new UpdateRmsIncidentReportRequest(rmsReport.Version, null, null, null, null, null, null, null, null, "Clarified records summary", "selftest:rms-clarify"), "records-user");
rmsReport = core.SubmitRmsForReview(rmsReport.Id, new SubmitRmsForReviewRequest(rmsReport.Version, "Resubmitted", "selftest:rms-submit-2"), "records-user");
rmsReport = core.ReviewRmsIncidentReport(rmsReport.Id, new ReviewRmsIncidentReportRequest(rmsReport.Version, "approve", "Approved", "selftest:rms-finalize"), "records-supervisor");
Check(rmsReport.Status == "Finalized" && rmsReport.FinalizedAtUtc is not null, "RMS review and finalization lifecycle");
var finalizedDirectEditRejected = false;
try { core.UpdateRmsIncidentReport(rmsReport.Id, new UpdateRmsIncidentReportRequest(rmsReport.Version, null, null, null, null, null, null, null, null, "destructive edit", "selftest:rms-final-edit"), "records-user"); }
catch (InvalidOperationException) { finalizedDirectEditRejected = true; }
Check(finalizedDirectEditRejected, "finalized RMS destructive edit rejection");
var noOpAmendmentRejected = false;
try { core.AmendRmsIncidentReport(rmsReport.Id, new AmendRmsIncidentReportRequest(rmsReport.Version, "Reason but no changed fields", null, null, null, null, null, null, null, null, rmsReport.Summary, "selftest:rms-noop-amend"), "records-user"); }
catch (InvalidOperationException) { noOpAmendmentRejected = true; }
Check(noOpAmendmentRejected, "RMS amendment requires content change");
rmsReport = core.AmendRmsIncidentReport(rmsReport.Id, new AmendRmsIncidentReportRequest(rmsReport.Version, "Post-final factual clarification", null, null, null, null, null, null, null, null, "Finalized summary with amendment", "selftest:rms-amend"), "records-user");
Check(rmsReport.Status == "Finalized" && rmsReport.Provenance.Any(x => x.Field == "Summary" && x.SourceKind == "RmsAmendment"), "RMS reasoned amendment provenance");
var rmsAttachment = core.AddRmsAttachmentReference(rmsReport.Id, new AddRmsAttachmentReferenceRequest("scene-photo-reference.jpg", "Operational", "dms://records/selftest/scene-photo-reference", "Reference only; content remains in DMS", "selftest:rms-attachment"), "records-user");
Check(core.GetRmsReportVersions(rmsReport.Id).Count >= 7 && core.GetRmsAttachments(rmsReport.Id).Any(x => x.Id == rmsAttachment.Id), "RMS immutable versions and attachment reference");
var duplicateRmsCreate = rmsReporting.CreateFromIncident(rmsCadIncident.Id, new CreateRmsFromIncidentRequest(agency, "FireIncident", "duplicate create attempt", "selftest:rms-create-duplicate"), "records-user");
Check(duplicateRmsCreate.Id == rmsReport.Id, "one canonical RMS report per incident/report type");


// Pass 19: RMS-linked NERIS projection and durable submission-state safety.
var nerisSubmission = core.BuildNerisSubmission(rmsReport.Id, new BuildNerisSubmissionRequest(null, "selftest:neris-build"), "records-user");
Check(nerisSubmission.State == "Ready" && nerisSubmission.RmsReportVersion == rmsReport.Version && nerisSubmission.PayloadSha256.Length == 64, "NERIS finalized RMS projection and payload hash");
Check(nerisSubmission.PayloadJson.Contains(rmsReport.IncidentNumber, StringComparison.Ordinal) && nerisSubmission.PayloadJson.Contains("DISRoute NERIS Bridge", StringComparison.Ordinal), "NERIS bridge payload mapping");
var duplicateNerisBuild = core.BuildNerisSubmission(rmsReport.Id, new BuildNerisSubmissionRequest(null, "selftest:neris-build-duplicate"), "records-user");
Check(duplicateNerisBuild.Id == nerisSubmission.Id, "one NERIS projection per RMS version/profile");
var unsupportedNerisProfileRejected = false;
try { core.BuildNerisSubmission(rmsReport.Id, new BuildNerisSubmissionRequest("unsupported-profile", "selftest:neris-bad-profile"), "records-user"); }
catch (InvalidOperationException) { unsupportedNerisProfileRejected = true; }
Check(unsupportedNerisProfileRejected, "unsupported NERIS profile rejection");
nerisSubmission = core.QueueNerisSubmission(nerisSubmission.Id, new QueueNerisSubmissionRequest("simulation-fire-reporting", "selftest:neris-queue"), "records-user");
Check(nerisSubmission.State == "Queued" && nerisSubmission.ProviderKey == "simulation-fire-reporting", "NERIS durable queue state");
var startedNeris = core.BeginNerisSubmissionAttempt(nerisSubmission.Id, "simulation-fire-reporting", "selftest-worker");
Check(startedNeris.Submission.State == "Submitting" && startedNeris.Attempt.AttemptNumber == 1 && startedNeris.Attempt.State == "Started", "NERIS durable in-flight attempt state");

var moduleDependencyBlocked = false;
try { core.SetModule("rms", false); } catch (InvalidOperationException) { moduleDependencyBlocked = true; }
Check(moduleDependencyBlocked, "module dependency enforcement");

// Pass 24: durable external gateway normalization and provider-operation safety.
var gateway911At = DateTimeOffset.UtcNow.AddMinutes(-2);
var callsBeforeGateway = features.Calls.Count;
var gateway911Request = new Ingest911GatewayEventRequest(agency, "simulation-911", "SELFTEST-CALL-24", "Open", "Gateway Caller", "555-0240", "240 Gateway Avenue", "Automatic alarm", gateway911At);
var gateway911Open = features.Ingest911GatewayEvent(gateway911Request, "dispatcher-a");
var gateway911Duplicate = features.Ingest911GatewayEvent(gateway911Request, "dispatcher-a");
Check(gateway911Duplicate.Id == gateway911Open.Id && features.Calls.Count == callsBeforeGateway + 1, "911 gateway duplicate event is idempotent and does not duplicate call intake");
var gateway911Answered = features.Ingest911GatewayEvent(gateway911Request with { EventState = "Answered", SourceTimestamp = gateway911At.AddSeconds(10) }, "dispatcher-a");
var gateway911Ended = features.Ingest911GatewayEvent(gateway911Request with { EventState = "Ended", SourceTimestamp = gateway911At.AddMinutes(1) }, "dispatcher-a");
var gatewayCall = features.Calls.Single(x => x.ProviderCallId == "SELFTEST-CALL-24");
Check(gateway911Answered.Id != gateway911Open.Id && gateway911Ended.Id != gateway911Answered.Id && gatewayCall.Disposition == "Ended" && gatewayCall.AnsweredAtUtc is not null && gatewayCall.EndedAtUtc is not null, "911 gateway lifecycle updates one normalized call record");

var radioEvent = features.IngestRadioGatewayEvent(new IngestRadioGatewayEventRequest(agency, "simulation-radio", "SELFTEST-RADIO-24", "Emergency", "RADIO-24", "Dispatch 1", unitIds[0], null, DateTimeOffset.UtcNow), "dispatcher-a");
var radioDuplicate = features.IngestRadioGatewayEvent(new IngestRadioGatewayEventRequest(agency, "simulation-radio", "SELFTEST-RADIO-24", "Emergency", "RADIO-24", "Dispatch 1", unitIds[0], null, radioEvent.SourceTimestamp), "dispatcher-a");
Check(radioDuplicate.Id == radioEvent.Id && features.Alerts.Count(x => x.CorrelationKey == radioEvent.CorrelationKey) == 1, "radio gateway duplicate emergency is idempotent and creates one critical alert");
Check(features.Alerts.Single(x => x.CorrelationKey == radioEvent.CorrelationKey).Severity == "Critical", "radio emergency maps to critical alert");

var aisEvent = features.IngestAisGatewayEvent(new IngestAisGatewayEventRequest(agency, "simulation-ais", "SELFTEST-AIS-24", "367240001", "Gateway Vessel", 40.721, -74.021, 7.5, 181, DateTimeOffset.UtcNow), "dispatcher-a");
var aisDuplicate = features.IngestAisGatewayEvent(new IngestAisGatewayEventRequest(agency, "simulation-ais", "SELFTEST-AIS-24", "367240001", "Gateway Vessel", 40.721, -74.021, 7.5, 181, aisEvent.SourceTimestamp), "dispatcher-a");
Check(aisDuplicate.Id == aisEvent.Id && features.Vessels.Any(x => x.Mmsi == "367240001" && x.Source == "simulation-ais"), "AIS gateway event is idempotent and updates vessel observation");

var confirmedIntegrationOperation = features.BeginIntegrationOperation(agency, "simulation-recording", "RecordingLookup", incident.Id, null, new { channel = "Dispatch 1" }, "dispatcher-a");
var confirmedIntegrationAttempt = features.MarkIntegrationSubmitting(confirmedIntegrationOperation.Id, "dispatcher-a");
confirmedIntegrationOperation = features.CompleteIntegrationAttempt(confirmedIntegrationOperation.Id, confirmedIntegrationAttempt.Attempt.Id, "Confirmed", "SELFTEST-REC-24", null, "dispatcher-a");
Check(confirmedIntegrationOperation.State == "Confirmed" && confirmedIntegrationOperation.ProviderReference == "SELFTEST-REC-24", "provider operation records confirmed external side effect with durable provider reference");
var recordingReference = features.AddRecordingReference(agency, incident.Id, "simulation-recording", "SELFTEST-REC-24", "Radio", DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow, "Self-test dispatch recording", "https://invalid.example/recording/SELFTEST-REC-24", "dispatcher-a");
var duplicateRecordingReference = features.AddRecordingReference(agency, incident.Id, "simulation-recording", "SELFTEST-REC-24", "Radio", DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow, "Duplicate self-test recording", null, "dispatcher-a");
Check(duplicateRecordingReference.Id == recordingReference.Id, "recording provider reference deduplicates across repeated lookup results");

var ambiguousIntegrationOperation = features.BeginIntegrationOperation(agency, "simulation-radio", "RadioClearEmergency", incident.Id, unitIds[0], new RadioEmergencyClearRequest(agency, incident.Id, unitIds[0], "RADIO-24", "simulation-radio", "selftest-integration-ambiguous"), "dispatcher-a");
var ambiguousIntegrationAttempt = features.MarkIntegrationSubmitting(ambiguousIntegrationOperation.Id, "dispatcher-a");
Check(ambiguousIntegrationAttempt.Operation.State == "Submitting" && ambiguousIntegrationAttempt.Attempt.State == "Submitting", "provider operation persists in-flight boundary before external side effect");

Check(features.VerifyAuditIntegrity().Valid, "tamper-evident audit chain");
Check(mutationQueue.Queued > 0, "durable mutation journal emission");

var operationalState = core.ExportState();
var featureState = features.ExportState();
var idempotencyState = idempotency.ExportState();
Check(idempotencyState.Count > 0, "durable idempotency receipt capture");
var restoredQueue = new OperationalMutationBuffer();
var restoredIdempotency = new DurableIdempotencyStore();
var restoredCore = new OperationalStore(restoredQueue, restoredIdempotency);
var restoredFeatures = new FeatureStore(restoredCore, restoredQueue, restoredIdempotency);
restoredCore.ImportState(operationalState);
restoredFeatures.ImportState(featureState);
restoredIdempotency.ImportState(idempotencyState);
Check(restoredCore.GetIncident(incident.Id).Number == incident.Number, "operational persistence round trip");
Check(restoredFeatures.GetCollaborativeNarrative(incident.Id).Text == shared.Text, "feature persistence round trip");
Check(restoredFeatures.GetCollaborativeNarrativeOperations(incident.Id).Count == narrativeOps.Count, "collaborative narrative operation history persistence");
Check(restoredFeatures.Rotations.Any(x => x.Id == rotation.Id), "workforce depth persistence round trip");
Check(restoredFeatures.StaffingRules.Any(x => x.Id == staffingDriverRule.Id) && restoredFeatures.StaffingRules.Any(x => x.Id == staffingOfficerRule.Id), "workforce staffing rule persistence round trip");
Check(restoredFeatures.WorkforceAssignments.Any(x => x.Id == filledDriverAssignment.Id && x.State == "Overtime") && restoredFeatures.WorkforceAssignments.Any(x => x.Id == reliefAssignment.Id), "effective workforce assignment persistence round trip");
Check(restoredFeatures.WorkforceRosterSnapshots.Count(x => x.AgencyId == agency && x.Date == workforceDate) >= 2 && restoredFeatures.WorkforceRosterSnapshots.Any(x => x.Id == postedRoster1.Id && x.ContentSha256 == postedRoster1.ContentSha256), "immutable workforce roster snapshot persistence round trip");
Check(restoredFeatures.WorkforceScheduleChanges.Any(x => x.AgencyId == agency && x.Date == workforceDate && x.Kind == "CallOut") && restoredFeatures.WorkforceScheduleChanges.Any(x => x.Kind == "Relief"), "structured workforce change history persistence round trip");
Check(restoredFeatures.GetWorkforceRoster(agency, workforceDate).Staffing.MeetsMinimums, "restored workforce roster recalculates qualification-aware staffing");
Check(restoredFeatures.Hazmat.Any(x => x.Id == hazmat.Id), "RMS depth persistence round trip");
Check(restoredFeatures.ApprovalWorkflows.Any(x => x.Id == workflow.Id && x.State == "Approved"), "approval workflow persistence round trip");
var restoredDmsRevisions = restoredFeatures.GetDmsRevisions(dmsDocument.Id);
Check(restoredDmsRevisions.Any(x => x.Version == 2 && x.State == "Published" && x.ContentSha256 == dmsRevision2.ContentSha256) && restoredDmsRevisions.Any(x => x.Version == 1 && x.State == "Superseded"), "DMS immutable revision history persistence round trip");
Check(restoredFeatures.GetDmsReadReceipts(dmsDocument.Id).Count == 3 && restoredFeatures.GetDmsReadReceipts(dmsDocument.Id).Any(x => x.Recipient == "reader-one" && x.State == "Acknowledged"), "DMS required-read receipt persistence round trip");
Check(restoredFeatures.GetDmsAssetReferences(dmsDocument.Id).Any(x => x.Id == dmsAsset.Id && x.ContentSha256 == dmsAsset.ContentSha256), "DMS asset reference persistence round trip");
Check(restoredFeatures.GetDmsDocumentLinks(dmsDocument.Id).Any(x => x.Id == dmsLink.Id), "DMS entity link persistence round trip");
Check(restoredFeatures.GetFormSignatures(form.Id).Single().ValuesSha256 == formSignature.ValuesSha256, "form signature hash persistence round trip");
Check(restoredFeatures.VerifyAuditIntegrity().Valid, "audit integrity persistence round trip");
Check(restoredFeatures.OperationalMessages.Any(x => x.Id == operationalMessage.Id), "operational messaging persistence round trip");
Check(restoredFeatures.MessageReceipts.Any(x => x.MessageId == operationalMessage.Id && x.State == "Acknowledged"), "message receipt persistence round trip");
Check(restoredFeatures.ChatChannels.Any(x => x.Id == directChat.Id && x.Archived) && restoredFeatures.ChatChannels.Any(x => x.Id == mutualAidChat.Id), "chat channel persistence round trip");
Check(restoredFeatures.ChatMessages.Any(x => x.Id == chatMessage.Id && x.Attachments.Count == 1) && restoredFeatures.ChatMessages.Any(x => x.Id == mutualMessage.Id), "chat message and attachment persistence round trip");
Check(restoredFeatures.ChatReadStates.Any(x => x.ChannelId == directChat.Id && x.UserId == "supervisor-a" && x.LastReadMessageId == chatMessage.Id), "chat read cursor persistence round trip");
Check(restoredFeatures.ChatChannels.Any(x => x.Id == groupChat.Id && x.OperationalGroupId == group.Id) && (restoredFeatures.OperationalGroups.Single(x => x.Id == group.Id).MemberUserIds ?? []).Count == 0, "chat operational-group binding and membership state persist round trip");
Check(restoredCore.RmsIncidentReports.Any(x => x.Id == rmsReport.Id && x.Status == "Finalized" && x.Summary == "Finalized summary with amendment"), "RMS incident report persistence round trip");
Check(restoredCore.GetRmsReportVersions(rmsReport.Id).Count == core.GetRmsReportVersions(rmsReport.Id).Count, "RMS immutable version persistence round trip");
Check(restoredCore.GetRmsAttachments(rmsReport.Id).Any(x => x.Id == rmsAttachment.Id), "RMS attachment reference persistence round trip");
Check(restoredFeatures.IntegrationEvents.Any(x => x.Id == gateway911Open.Id) && restoredFeatures.IntegrationEvents.Any(x => x.Id == radioEvent.Id) && restoredFeatures.IntegrationEvents.Any(x => x.Id == aisEvent.Id), "normalized integration gateway events persist across restart/import");
Check(restoredFeatures.RecordingReferences.Any(x => x.Id == recordingReference.Id && x.ProviderReference == "SELFTEST-REC-24"), "recording reference persists across restart/import");
Check(restoredFeatures.IntegrationOperations.Any(x => x.Id == confirmedIntegrationOperation.Id && x.State == "Confirmed"), "confirmed provider operation persists across restart/import");
var restoredAmbiguousIntegration = restoredFeatures.IntegrationOperations.Single(x => x.Id == ambiguousIntegrationOperation.Id);
Check(restoredAmbiguousIntegration.State == "NeedsReconciliation" && restoredAmbiguousIntegration.PossibleDuplicate, "in-flight provider operation becomes NeedsReconciliation after restore/failover");
Check(restoredFeatures.IntegrationAttempts.Single(x => x.Id == ambiguousIntegrationAttempt.Attempt.Id).State == "UnknownAfterRestore", "ambiguous provider attempt history is retained without blind retry");
restoredAmbiguousIntegration = restoredFeatures.ReconcileIntegrationOperation(restoredAmbiguousIntegration.Id, new ReconcileIntegrationOperationRequest("Not-Sent", null, "Provider confirmed command was not accepted"), "dispatcher-a");
Check(restoredAmbiguousIntegration.State == "Failed" && !restoredAmbiguousIntegration.PossibleDuplicate, "operator reconciliation clears ambiguous duplicate risk before retry");

var restoredNeris = restoredCore.GetNerisSubmission(nerisSubmission.Id);
Check(restoredNeris.State == "NeedsReconciliation" && restoredNeris.AttemptCount == 1, "in-flight NERIS restore requires reconciliation");
Check(restoredCore.GetNerisSubmissionAttempts(restoredNeris.Id).Single().State == "UnknownAfterRestore", "NERIS in-flight attempt history survives restore without blind retry");
var ambiguousRetryRejected = false;
try { restoredCore.RetryNerisSubmission(restoredNeris.Id, new RetryNerisSubmissionRequest(null, false, "selftest:neris-unsafe-retry"), "records-user"); }
catch (InvalidOperationException) { ambiguousRetryRejected = true; }
Check(ambiguousRetryRejected, "ambiguous NERIS retry requires duplicate-risk acknowledgement");
restoredNeris = restoredCore.ReconcileNerisSubmission(restoredNeris.Id, new ReconcileNerisSubmissionRequest("failed", null, "Provider confirmed no receipt", "selftest:neris-reconcile"), "records-supervisor");
restoredNeris = restoredCore.RetryNerisSubmission(restoredNeris.Id, new RetryNerisSubmissionRequest("simulation-fire-reporting", false, "selftest:neris-retry"), "records-user");
Check(restoredNeris.State == "Queued" && restoredCore.GetNerisSubmissionAttempts(restoredNeris.Id).Count == 1, "reconciled NERIS failure can be safely retried with immutable prior attempt");
Check(restoredFeatures.Maydays.Any(x => x.Id == mayday.Id && x.State == "Escalated" && x.AssignedTo == "Division Supervisor" && x.EscalationLevel == 1), "active Mayday persistence round trip");
var restoredMayday = restoredFeatures.ActOnMayday(mayday.Id, "resolve", "selftest-supervisor", new MaydayActionRequest(null, "Member located and removed"));
Check(restoredMayday.State == "Resolved" && restoredMayday.ResolvedAtUtc is not null, "restored Mayday resolution");
Check(restoredFeatures.Alerts.Single(x => x.CorrelationKey == $"mayday:{mayday.Id:D}").State == "Resolved", "Mayday resolution synchronizes critical alert");
var restoredIncidentCount = restoredCore.GetIncidents().Count;
var replayedCreate = restoredCore.CreateIncident(createRequest, "selftest");
Check(replayedCreate.Id == incident.Id && restoredCore.GetIncidents().Count == restoredIncidentCount, "create idempotency survives restart/import");
var restoredDispatchCount = restoredFeatures.DispatchActions.Count;
var replayedDispatch = restoredFeatures.CommitDispatch(incident.Id, dispatchRequest, "selftest");
Check(replayedDispatch.Action.Id == dispatch.Action.Id && restoredFeatures.DispatchActions.Count == restoredDispatchCount, "dispatch idempotency survives restart/import");
var replayedMobile = restoredFeatures.MobileSync(new MobileSyncRequest(agency, mobileDevice.DeviceId, null, [mobileNarrativeOperation], unitIds[0]), "mobile-user");
Check(replayedMobile.OperationResults.Single().State == "DuplicateIgnored", "mobile operation idempotency survives restart/import");
Check(replayedMobile.OperationalPackage?.CurrentIncident?.Id == incident.Id && replayedMobile.OperationalPackage.Crew.Any(x => x.Id == mobileAssignment.Id), "mobile assignment-scoped operational package survives restart/import");
var idempotencyCollisionRejected = false;
try
{
    restoredCore.CreateIncident(createRequest with { Address = "DIFFERENT PAYLOAD" }, "selftest");
}
catch (InvalidOperationException)
{
    idempotencyCollisionRejected = true;
}
Check(idempotencyCollisionRejected && restoredCore.GetIncidents().Count == restoredIncidentCount, "idempotency key payload collision rejection");
var restoredReplayService = new ReplayService(restoredCore, restoredFeatures);
var restoredHistoricalReplay = restoredReplayService.GetIncidentSnapshot(incident.Id, beforeCorrectionUtc);
Check(restoredHistoricalReplay.Incident.Address == "500 Test Avenue", "historical replay survives persistence round trip");
Check(restoredHistoricalReplay.CorrectedLater.Any(x => x.Id == correction.Id), "replay correction history survives persistence round trip");
Check(restoredHistoricalReplay.DispatchActions.Count == historicalReplay.DispatchActions.Count, "replay dispatch history survives persistence round trip");

incident = core.GetIncident(incident.Id);
var split = features.SplitIncident(incident.Id, new SplitIncidentRequest(incident.Version, "Related Alarm", "Routine", "502 Test Avenue", "Split self test", "selftest", "selftest:split"));
Check(features.IncidentRelationships.Any(x => x.SourceIncidentId == split.Id && x.TargetIncidentId == incident.Id), "incident split relationship");

var relationshipAgencyMismatchRejected = false;
try
{
    features.CreateIncidentRelationship(split.Id, new CreateIncidentRelationshipRequest(Guid.NewGuid(), incident.Id, "Related", "selftest"));
}
catch (InvalidOperationException)
{
    relationshipAgencyMismatchRejected = true;
}
Check(relationshipAgencyMismatchRejected, "incident relationship agency mismatch rejection");

var mergeUnit = core.Units.First(x => x.IncidentId is null);
var mergeSource = core.CreateIncident(new CreateIncidentRequest(agency, station.Id, "Merge Source", "Routine", "600 Test Avenue", "", "Test City", "", "", "", "selftest:merge-source"), "selftest");
var mergeTarget = core.CreateIncident(new CreateIncidentRequest(agency, station.Id, "Merge Target", "Routine", "602 Test Avenue", "", "Test City", "", "", "", "selftest:merge-target"), "selftest");
mergeSource = core.AssignUnit(mergeSource.Id, new AssignUnitRequest(mergeUnit.Id, mergeSource.Version, true, "selftest:merge-unit"), "selftest");
var mergeRequest = new MergeIncidentsRequest(mergeTarget.Id, mergeSource.Version, mergeTarget.Version, "Duplicate call confirmed", "selftest", "selftest:merge");
var mergedSource = features.MergeIncidents(mergeSource.Id, mergeRequest);
Check(mergedSource.Status == "Closed", "incident merge closes source");
Check(core.Units.First(x => x.Id == mergeUnit.Id).IncidentId == mergeTarget.Id, "incident merge transfers assigned units");
Check(features.IncidentRelationships.Any(x => x.SourceIncidentId == mergeSource.Id && x.TargetIncidentId == mergeTarget.Id && x.RelationshipType == "MergedInto"), "incident merge relationship");
var mergedSourceRetry = features.MergeIncidents(mergeSource.Id, mergeRequest);
Check(mergedSourceRetry.Id == mergedSource.Id && mergedSourceRetry.Version == mergedSource.Version, "incident merge durable idempotent retry");


// Pass 27: production deployment safety and secret references.
{
    var secretRoot = Path.Combine(Path.GetTempPath(), "disroute-selftest-secrets-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(secretRoot);
    var secretPath = Path.Combine(secretRoot, "db.txt");
    File.WriteAllText(secretPath, "Host=localhost;Database=disroute;Username=disroute;Password=test-secret\n");
    if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(secretPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    Environment.SetEnvironmentVariable("DISROUTE_SELFTEST_SECRET", "environment-secret");
    try
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Security:Secrets:Directory"] = secretRoot,
            ["ConnectionStrings:DISRoute"] = "secret://file/db.txt",
            ["Test:EnvSecret"] = "secret://env/DISROUTE_SELFTEST_SECRET",
            ["Persistence:Provider"] = "PostgreSQL",
            ["Security:RequireAuthentication"] = "true",
            ["Security:RequireHttps"] = "true",
            ["Security:RequireMfa"] = "true",
            ["Security:MaxRequestBodyBytes"] = "10485760",
            ["Security:RateLimit:PermitLimitPerMinute"] = "600",
            ["Security:ReverseProxy:Enabled"] = "true",
            ["Security:ReverseProxy:KnownProxies:0"] = "127.0.0.1",
            ["HighAvailability:DatabaseTopology"] = "SharedPrimary",
            ["HighAvailability:DatabaseFencingMode"] = "None"
        }).Build();
        Check(SecretReferenceResolver.ResolveConnectionString(cfg).Contains("Password=test-secret", StringComparison.Ordinal), "file-backed PostgreSQL secret reference resolves");
        Check(SecretReferenceResolver.ResolveRequired(cfg, "Test:EnvSecret") == "environment-secret", "environment-backed secret reference resolves");
        ProductionDeploymentSafety.ValidateStartup(cfg, new SelfTestHostEnvironment("Production"));
        Check(ProductionDeploymentSafety.ConfigurationFingerprint(cfg).Length == 64, "deployment configuration fingerprint is SHA-256");

        var unsafeProxy = new ConfigurationBuilder().AddInMemoryCollection(cfg.AsEnumerable().ToDictionary(x => x.Key, x => x.Value)).AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Security:ReverseProxy:Enabled"] = "true",
            ["Security:ReverseProxy:KnownProxies:0"] = ""
        }).Build();
        var proxyRejected = false;
        try { ProductionDeploymentSafety.ValidateStartup(unsafeProxy, new SelfTestHostEnvironment("Production")); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("KnownProxies", StringComparison.Ordinal)) { proxyRejected = true; }
        Check(proxyRejected, "production reverse proxy refuses untrusted forwarded headers");

        var plaintextProvider = new ConfigurationBuilder().AddInMemoryCollection(cfg.AsEnumerable().ToDictionary(x => x.Key, x => x.Value)).AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Integrations:StationAlert:BearerToken"] = "plaintext-not-allowed"
        }).Build();
        var providerSecretRejected = false;
        try { ProductionDeploymentSafety.ValidateStartup(plaintextProvider, new SelfTestHostEnvironment("Production")); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("secret://", StringComparison.Ordinal)) { providerSecretRejected = true; }
        Check(providerSecretRejected, "production provider credential must be a secret reference");
    }
    finally
    {
        Environment.SetEnvironmentVariable("DISROUTE_SELFTEST_SECRET", null);
        Directory.Delete(secretRoot, true);
    }
}

Console.WriteLine($"DISRoute self-test PASS — incidents={core.GetIncidents().Count}, units={core.Units.Count}, audit={features.Audit.Count}, alerts={features.Alerts.Count}");

sealed class SelfTestHostEnvironment : Microsoft.Extensions.Hosting.IHostEnvironment
{
    public SelfTestHostEnvironment(string environmentName) { EnvironmentName = environmentName; ContentRootPath = Directory.GetCurrentDirectory(); }
    public string EnvironmentName { get; set; }
    public string ApplicationName { get; set; } = "DISRoute.SelfTest";
    public string ContentRootPath { get; set; }
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
}
