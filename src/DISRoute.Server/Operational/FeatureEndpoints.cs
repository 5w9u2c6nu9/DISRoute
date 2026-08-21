using DISRoute.Integrations;
using DISRoute.Contracts.Operational;
using DISRoute.Server.Realtime;
using Microsoft.AspNetCore.SignalR;

namespace DISRoute.Server.Operational;

public static class FeatureEndpoints
{
    public static RouteGroupBuilder MapFeatureEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/presence", (HttpContext context, FeatureStore store) => Try(() =>
        {
            var agencyId = store.ResolveActorAgency(Actor(context));
            return Results.Ok(OperationsHub.CurrentPresence.Where(x => x.AgencyId == agencyId).ToArray());
        }));
        api.MapGet("/workspaces", (string userId, string? workstationId, FeatureStore store) => Results.Ok(store.GetWorkspaces(userId, workstationId)));
        api.MapPost("/workspaces", (SaveWorkspaceLayoutRequest request, FeatureStore store) => Try(() => Results.Ok(store.SaveWorkspace(null, request))));
        api.MapPut("/workspaces/{id:guid}", (Guid id, SaveWorkspaceLayoutRequest request, FeatureStore store) => Try(() => Results.Ok(store.SaveWorkspace(id, request))));
        api.MapGet("/themes", (FeatureStore store) => Results.Ok(store.Themes));
        api.MapPost("/themes", (UpsertThemePackageRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.UpsertTheme(null, request, Actor(context)))));
        api.MapPut("/themes/{id:guid}", (Guid id, UpsertThemePackageRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.UpsertTheme(id, request, Actor(context)))));

        api.MapPost("/cad/duplicate-check", (DuplicateCheckRequest request, FeatureStore store) => Results.Ok(store.FindDuplicates(request)));
        api.MapPost("/cad/response-recommendation", (ResponseRecommendationRequest request, FeatureStore store) => Try(() => Results.Ok(store.RecommendResponse(request))));
        api.MapGet("/cad/response-plans", (FeatureStore store) => Results.Ok(store.ResponsePlans));
        api.MapPost("/cad/response-plans", (UpsertResponsePlanRequest request, FeatureStore store) => Try(() => Results.Ok(store.UpsertResponsePlan(null, request))));
        api.MapPut("/cad/response-plans/{id:guid}", (Guid id, UpsertResponsePlanRequest request, FeatureStore store) => Try(() => Results.Ok(store.UpsertResponsePlan(id, request))));
        api.MapGet("/incidents/relationships", (Guid? incidentId, FeatureStore store) => Results.Ok(incidentId is Guid iid ? store.IncidentRelationships.Where(x => x.SourceIncidentId == iid || x.TargetIncidentId == iid).ToArray() : store.IncidentRelationships));
        api.MapPost("/incidents/{id:guid}/relationships", async (Guid id, CreateIncidentRelationshipRequest request, FeatureStore store, IHubContext<OperationsHub> hub) =>
        {
            try
            {
                var relationship = store.CreateIncidentRelationship(id, request);
                await hub.Clients.Group($"agency:{relationship.AgencyId:D}").SendAsync("incidentRelationshipChanged", relationship);
                await hub.Clients.Group($"incident:{relationship.SourceIncidentId:D}").SendAsync("incidentRelationshipChanged", relationship);
                await hub.Clients.Group($"incident:{relationship.TargetIncidentId:D}").SendAsync("incidentRelationshipChanged", relationship);
                return Results.Ok(relationship);
            }
            catch (Exception ex) { return Error(ex); }
        });
        api.MapPost("/incidents/{id:guid}/merge", async (Guid id, MergeIncidentsRequest request, FeatureStore store, OperationalStore core, IHubContext<OperationsHub> hub) =>
        {
            try
            {
                var closedSource = store.MergeIncidents(id, request);
                var target = core.GetIncident(request.TargetIncidentId);
                var relationship = store.IncidentRelationships.Where(x => x.SourceIncidentId == id && x.TargetIncidentId == request.TargetIncidentId && x.RelationshipType == "MergedInto").OrderByDescending(x => x.CreatedAtUtc).FirstOrDefault();
                await hub.Clients.Group($"agency:{closedSource.AgencyId:D}").SendAsync("incidentChanged", closedSource);
                await hub.Clients.Group($"agency:{target.AgencyId:D}").SendAsync("incidentChanged", target);
                await hub.Clients.Group($"agency:{target.AgencyId:D}").SendAsync("unitsChanged", core.Units);
                await hub.Clients.Group($"incident:{closedSource.Id:D}").SendAsync("incidentChanged", closedSource);
                await hub.Clients.Group($"incident:{target.Id:D}").SendAsync("incidentChanged", target);
                if (relationship is not null) await hub.Clients.Group($"agency:{relationship.AgencyId:D}").SendAsync("incidentRelationshipChanged", relationship);
                return Results.Ok(closedSource);
            }
            catch (Exception ex) { return Error(ex); }
        });
        api.MapPost("/incidents/{id:guid}/split", async (Guid id, SplitIncidentRequest request, FeatureStore store, IHubContext<OperationsHub> hub) =>
        {
            try
            {
                var created = store.SplitIncident(id, request);
                var relationship = store.IncidentRelationships.Where(x => x.SourceIncidentId == created.Id && x.TargetIncidentId == id && x.RelationshipType == "SplitFrom").OrderByDescending(x => x.CreatedAtUtc).FirstOrDefault();
                await hub.Clients.Group($"agency:{created.AgencyId:D}").SendAsync("incidentChanged", created);
                await hub.Clients.Group($"incident:{created.Id:D}").SendAsync("incidentChanged", created);
                if (relationship is not null) await hub.Clients.Group($"agency:{relationship.AgencyId:D}").SendAsync("incidentRelationshipChanged", relationship);
                return Results.Ok(created);
            }
            catch (Exception ex) { return Error(ex); }
        });
        api.MapGet("/incident-operations/groups", (Guid? incidentId, FeatureStore store) => Results.Ok(incidentId is Guid iid ? store.OperationalGroups.Where(x => x.IncidentId == iid).ToArray() : store.OperationalGroups));
        api.MapPost("/incident-operations/groups", (CreateOperationalGroupRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.CreateOperationalGroup(request, Actor(context)))));
        api.MapPost("/incident-operations/groups/{id:guid}/members", (Guid id, UpdateOperationalGroupMembersRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.SetOperationalGroupMembers(id, request, Actor(context)))));
        api.MapGet("/incident-operations/command", (Guid? incidentId, FeatureStore store) => Results.Ok(incidentId is Guid iid ? store.Commands.Where(x => x.IncidentId == iid).ToArray() : store.Commands));
        api.MapPost("/incident-operations/command", (SetCommandRequest request, FeatureStore store) => Try(() => Results.Ok(store.SetCommand(request))));
        api.MapGet("/mutual-aid", (Guid? incidentId, FeatureStore store) => Results.Ok(incidentId is Guid iid ? store.MutualAid.Where(x => x.IncidentId == iid).ToArray() : store.MutualAid));
        api.MapPost("/mutual-aid", (CreateMutualAidRequest request, FeatureStore store) => Try(() => Results.Ok(store.CreateMutualAid(request))));
        api.MapPost("/mutual-aid/{id:guid}/{state}", (Guid id, string state, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.UpdateMutualAid(id, state, Actor(context)))));
        api.MapGet("/mayday", (Guid? incidentId, FeatureStore store) => Results.Ok(incidentId is Guid iid ? store.Maydays.Where(x => x.IncidentId == iid).ToArray() : store.Maydays));
        api.MapPost("/mayday", async (DeclareMaydayRequest request, HttpContext context, FeatureStore store, IHubContext<OperationsHub> hub) =>
        {
            try
            {
                var item = store.DeclareMayday(request with { DeclaredBy = Actor(context) });
                await hub.Clients.Group($"agency:{item.AgencyId:D}").SendAsync("maydayChanged", item);
                await hub.Clients.Group($"incident:{item.IncidentId:D}").SendAsync("maydayChanged", item);
                return Results.Ok(item);
            }
            catch (Exception ex) { return Error(ex); }
        });
        api.MapPost("/mayday/{id:guid}/{action}", async (Guid id, string action, MaydayActionRequest request, HttpContext context, FeatureStore store, IHubContext<OperationsHub> hub) =>
        {
            try
            {
                if (action is not ("acknowledge" or "assign" or "escalate" or "resolve")) return Results.BadRequest(new { error = "Unsupported Mayday action." });
                var item = store.ActOnMayday(id, action, Actor(context), request);
                await hub.Clients.Group($"agency:{item.AgencyId:D}").SendAsync("maydayChanged", item);
                await hub.Clients.Group($"incident:{item.IncidentId:D}").SendAsync("maydayChanged", item);
                return Results.Ok(item);
            }
            catch (Exception ex) { return Error(ex); }
        });

        api.MapGet("/incidents/{id:guid}/collaborative-narrative", (Guid id, FeatureStore store) => Try(() => Results.Ok(store.GetCollaborativeNarrative(id))));
        api.MapGet("/incidents/{id:guid}/collaborative-narrative/history", (Guid id, long? afterVersion, FeatureStore store) => Try(() => Results.Ok(store.GetCollaborativeNarrativeOperations(id, afterVersion ?? -1))));
        api.MapPost("/incidents/{id:guid}/collaborative-narrative/patch", async (Guid id, CollaborativeNarrativePatchRequest request, FeatureStore store, IHubContext<OperationsHub> hub) =>
        {
            try
            {
                var updated = store.PatchCollaborativeNarrative(id, request);
                await hub.Clients.Group($"incident:{id:D}").SendAsync("collaborativeNarrativeChanged", updated);
                return Results.Ok(updated);
            }
            catch (Exception ex) { return Error(ex); }
        });
        api.MapPut("/incidents/{id:guid}/collaborative-narrative", async (Guid id, ReplaceCollaborativeNarrativeRequest request, FeatureStore store, IHubContext<OperationsHub> hub) =>
        {
            try
            {
                var updated = store.ReplaceCollaborativeNarrative(id, request);
                await hub.Clients.Group($"incident:{id:D}").SendAsync("collaborativeNarrativeChanged", updated);
                return Results.Ok(updated);
            }
            catch (Exception ex) { return Error(ex); }
        });

        api.MapGet("/calls", (FeatureStore store) => Results.Ok(store.Calls));
        api.MapPost("/calls", (CreateCallRecordRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.CreateCall(request, Actor(context)))));
        api.MapPost("/calls/{id:guid}/link", (Guid id, LinkCallRequest request, FeatureStore store) => Try(() => Results.Ok(store.LinkCall(id, request))));

        api.MapGet("/dispatch/additional-alarms", (Guid? incidentId, FeatureStore store) => Results.Ok(incidentId is Guid iid ? store.AdditionalAlarms.Where(x => x.IncidentId == iid).ToArray() : store.AdditionalAlarms));
        api.MapPost("/incidents/{id:guid}/additional-alarm", async (Guid id, CommitAdditionalAlarmRequest request, FeatureStore store, IHubContext<OperationsHub> hub) =>
        {
            try
            {
                var created = store.CommitAdditionalAlarm(id, request);
                await hub.Clients.Group($"incident:{id:D}").SendAsync("additionalAlarmCommitted", created);
                return Results.Ok(created);
            }
            catch (Exception ex) { return Error(ex); }
        });
        api.MapPost("/dispatch/actions/{id:guid}/cancel", async (Guid id, CancelDispatchRequest request, FeatureStore store, IHubContext<OperationsHub> hub) =>
        {
            try
            {
                var updated = store.CancelDispatch(id, request);
                await hub.Clients.Group($"incident:{updated.IncidentId:D}").SendAsync("dispatchCancelled", updated);
                return Results.Ok(updated);
            }
            catch (Exception ex) { return Error(ex); }
        });
        api.MapGet("/avl/observations", (Guid? unitId, FeatureStore store) => Results.Ok(unitId is Guid uid ? store.AvlObservations.Where(x => x.UnitId == uid).ToArray() : store.AvlObservations.Take(1000).ToArray()));
        api.MapGet("/avl/advisories", (bool? activeOnly, FeatureStore store) => Results.Ok((activeOnly ?? true) ? store.AvlAdvisories.Where(x => x.State == "Open").ToArray() : store.AvlAdvisories));
        api.MapPost("/avl/observations", async (UpsertAvlObservationRequest request, FeatureStore store, IHubContext<OperationsHub> hub) =>
        {
            try
            {
                var result = store.ObserveAvl(request);
                await hub.Clients.Group($"agency:{request.AgencyId:D}").SendAsync("avlObservation", result.Observation);
                if (result.Advisory is not null) await hub.Clients.Group($"agency:{request.AgencyId:D}").SendAsync("avlAdvisory", result.Advisory);
                return Results.Ok(new { observation = result.Observation, advisory = result.Advisory });
            }
            catch (Exception ex) { return Error(ex); }
        });
        api.MapPost("/avl/advisories/{id:guid}/resolve", (Guid id, ResolveAvlAdvisoryRequest request, FeatureStore store) => Try(() => Results.Ok(store.ResolveAvlAdvisory(id, request))));
        api.MapGet("/incidents/{id:guid}/corrections", (Guid id, FeatureStore store) => Results.Ok(store.Corrections.Where(x => x.IncidentId == id).ToArray()));
        api.MapPost("/incidents/{id:guid}/corrections", async (Guid id, CorrectIncidentFieldRequest request, FeatureStore store, OperationalStore core, IHubContext<OperationsHub> hub) =>
        {
            try
            {
                var correction = store.CorrectIncident(id, request);
                var incident = core.GetIncident(id);
                await hub.Clients.Group($"incident:{id:D}").SendAsync("incidentCorrection", correction);
                await hub.Clients.Group($"incident:{id:D}").SendAsync("incidentChanged", incident);
                await hub.Clients.Group($"agency:{incident.AgencyId:D}").SendAsync("incidentChanged", incident);
                return Results.Ok(correction);
            }
            catch (Exception ex) { return Error(ex); }
        });

        api.MapGet("/dispatch/actions", (FeatureStore store) => Results.Ok(store.DispatchActions));
        api.MapGet("/dispatch/deliveries", (FeatureStore store) => Results.Ok(store.DispatchDeliveries));
        api.MapPost("/incidents/{id:guid}/dispatch", async (Guid id, CommitDispatchRequest request, HttpContext context, FeatureStore store, IHubContext<OperationsHub> hub) =>
        {
            try
            {
                var result = store.CommitDispatch(id, request, Actor(context));
                await hub.Clients.Group($"agency:{result.Incident.AgencyId:D}").SendAsync("dispatchCommitted", result.Action, result.Deliveries);
                await hub.Clients.Group($"incident:{id:D}").SendAsync("dispatchCommitted", result.Action, result.Deliveries);
                return Results.Ok(new { action = result.Action, deliveries = result.Deliveries, incident = result.Incident });
            }
            catch (Exception ex) { return Error(ex); }
        });
        api.MapPost("/dispatch/deliveries/{id:guid}/retry", async (Guid id, RetryDeliveryRequest request, HttpContext context, FeatureStore store, IHubContext<OperationsHub> hub) =>
        {
            try
            {
                var delivery = store.RetryDelivery(id, request with { Actor = Actor(context) });
                var action = store.DispatchActions.First(x => x.Id == delivery.DispatchActionId);
                await hub.Clients.Group($"agency:{action.AgencyId:D}").SendAsync("dispatchDeliveryChanged", delivery);
                await hub.Clients.Group($"incident:{action.IncidentId:D}").SendAsync("dispatchDeliveryChanged", delivery);
                return Results.Ok(delivery);
            }
            catch (Exception ex) { return Error(ex); }
        });
        api.MapPost("/dispatch/deliveries/{id:guid}/complete", (Guid id, CompleteDeliveryRequest request, FeatureStore store) => Try(() => Results.Ok(store.CompleteDelivery(id, request.Success, request.Error, request.Actor))));

        api.MapGet("/fire-watch", (FeatureStore store) => Results.Ok(store.FireWatches));
        api.MapPost("/fire-watch", (CreateFireWatchRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.CreateFireWatch(request, Actor(context)))));
        api.MapGet("/fire-watch/{id:guid}/checks", (Guid id, FeatureStore store) => Results.Ok(store.GetFireWatchChecks(id)));
        api.MapPost("/fire-watch/{id:guid}/checks", (Guid id, NotesActionRequest request, FeatureStore store) => Try(() => Results.Ok(store.AddFireWatchCheck(id, request.Actor, request.Notes ?? string.Empty))));
        api.MapPost("/fire-watch/{id:guid}/end", (Guid id, NotesActionRequest request, FeatureStore store) => Try(() => Results.Ok(store.EndFireWatch(id, request.Actor))));

        api.MapGet("/relocations", (FeatureStore store) => Results.Ok(store.Relocations));
        api.MapPost("/relocations", (CreateRelocationRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.CreateRelocation(request, Actor(context)))));
        api.MapPost("/relocations/{id:guid}/end", (Guid id, NotesActionRequest request, FeatureStore store) => Try(() => Results.Ok(store.EndRelocation(id, request.Actor))));

        api.MapGet("/premises", (FeatureStore store) => Results.Ok(store.Premises));
        api.MapPost("/premises", (UpsertPremiseRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.UpsertPremise(null, request, Actor(context)))));
        api.MapPut("/premises/{id:guid}", (Guid id, UpsertPremiseRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.UpsertPremise(id, request, Actor(context)))));
        api.MapGet("/preplans", (Guid? premiseId, FeatureStore store) => Results.Ok(premiseId is Guid id ? store.Preplans.Where(x => x.PremiseId == id).ToArray() : store.Preplans));
        api.MapPost("/preplans", (CreatePreplanRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.CreatePreplan(request, Actor(context)))));
        api.MapPost("/preplans/{id:guid}/publish", (Guid id, NotesActionRequest request, FeatureStore store) => Try(() => Results.Ok(store.PublishPreplan(id, request.Actor))));
        api.MapGet("/crg-assets", (Guid? premiseId, FeatureStore store) => Results.Ok(premiseId is Guid id ? store.CrgAssets.Where(x => x.PremiseId == id).ToArray() : store.CrgAssets));
        api.MapPost("/crg-assets", (RegisterCrgAssetRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.RegisterCrg(request, Actor(context)))));
        api.MapGet("/gis/hydrants", (FeatureStore store) => Results.Ok(store.Hydrants));
        api.MapPost("/gis/hydrants", (UpsertHydrantRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.UpsertHydrant(null, request, Actor(context)))));
        api.MapPut("/gis/hydrants/{id:guid}", (Guid id, UpsertHydrantRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.UpsertHydrant(id, request, Actor(context)))));
        api.MapGet("/gis/map-objects", (Guid? incidentId, FeatureStore store) => Results.Ok(incidentId is Guid id ? store.MapObjects.Where(x => x.IncidentId == id).ToArray() : store.MapObjects));
        api.MapGet("/incidents/{id:guid}/site-intelligence", (Guid id, FeatureStore store) => Try(() => Results.Ok(store.GetSiteIntelligence(id))));
        api.MapPost("/gis/map-objects", async (CreateMapObjectRequest request, HttpContext context, FeatureStore store, IHubContext<OperationsHub> hub) =>
        {
            try
            {
                var item = store.CreateMapObject(request, Actor(context));
                await hub.Clients.Group($"agency:{item.AgencyId:D}").SendAsync("mapObjectChanged", item);
                return Results.Ok(item);
            }
            catch (Exception ex) { return Error(ex); }
        });
        api.MapPost("/gis/route", (RouteRequest request, FeatureStore store) => Results.Ok(store.CalculateRoute(request)));
        api.MapGet("/marine/vessels", (FeatureStore store) => Results.Ok(store.Vessels));
        api.MapPut("/marine/vessels/{mmsi}", (string mmsi, UpsertVesselObservationRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.UpsertVessel(request with { Mmsi = mmsi }, Actor(context)))));

        api.MapGet("/alerts", (bool? activeOnly, FeatureStore store) => Results.Ok((activeOnly ?? true) ? store.Alerts.Where(x => x.State != "Resolved").ToArray() : store.Alerts));
        api.MapPost("/alerts", async (CreateAlertRequest request, HttpContext context, FeatureStore store, IHubContext<OperationsHub> hub) =>
        {
            try
            {
                var item = store.CreateAlert(request, Actor(context));
                await hub.Clients.Group($"agency:{item.AgencyId:D}").SendAsync("alertChanged", item);
                return Results.Ok(item);
            }
            catch (Exception ex) { return Error(ex); }
        });
        foreach (var action in new[] { "acknowledge", "claim", "resolve", "escalate" })
        {
            var captured = action;
            api.MapPost($"/alerts/{{id:guid}}/{captured}", async (Guid id, AlertActionRequest request, FeatureStore store, IHubContext<OperationsHub> hub) =>
            {
                try
                {
                    var item = store.ActOnAlert(id, captured, request);
                    await hub.Clients.Group($"agency:{item.AgencyId:D}").SendAsync("alertChanged", item);
                    return Results.Ok(item);
                }
                catch (Exception ex) { return Error(ex); }
            });
        }
        api.MapGet("/messaging/operational", (Guid? agencyId, Guid? incidentId, string? recipient, string? channel, FeatureStore store) => Results.Ok(store.GetOperationalMessages(agencyId ?? OperationalStore.DefaultAgencyId, incidentId, recipient, channel)));
        api.MapPost("/messaging/operational", async (CreateOperationalMessageRequest request, HttpContext context, FeatureStore store, IHubContext<OperationsHub> hub) =>
        {
            try
            {
                var item = store.SendOperationalMessage(request, Actor(context));
                await hub.Clients.Group($"agency:{item.AgencyId:D}").SendAsync("operationalMessage", item);
                if (item.IncidentId is Guid iid) await hub.Clients.Group($"incident:{iid:D}").SendAsync("operationalMessage", item);
                return Results.Ok(item);
            }
            catch (Exception ex) { return Error(ex); }
        });
        api.MapGet("/messaging/receipts/queued", (string recipient, string? deviceId, FeatureStore store) => Results.Ok(store.GetQueuedReceipts(recipient, deviceId)));
        api.MapPost("/messaging/receipts/{id:guid}", (Guid id, UpdateMessageReceiptRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.UpdateMessageReceipt(id, request, Actor(context)))));
        api.MapPost("/messaging/operational/{id:guid}/promote-to-narrative", async (Guid id, PromoteMessageToNarrativeRequest request, FeatureStore store, IHubContext<OperationsHub> hub) =>
        {
            try
            {
                var activity = store.PromoteMessageToNarrative(id, request);
                await hub.Clients.Group($"incident:{activity.IncidentId:D}").SendAsync("incidentActivity", activity);
                return Results.Ok(activity);
            }
            catch (Exception ex) { return Error(ex); }
        });

        api.MapGet("/chat/channels", (Guid? agencyId, bool? includeArchived, HttpContext context, FeatureStore store) => Try(() =>
        {
            var actor = Actor(context);
            var actorAgency = store.ResolveActorAgency(actor);
            return Results.Ok(store.GetChatChannels(agencyId ?? actorAgency, actor, includeArchived ?? false));
        }));
        api.MapPost("/chat/channels", async (CreateChatChannelRequest request, HttpContext context, FeatureStore store, IHubContext<OperationsHub> hub) =>
        {
            try
            {
                var actor = Actor(context);
                var item = store.CreateChatChannel(request, actor);
                if (item.Type == "Agency") await hub.Clients.Group($"agency:{item.OwnerAgencyId:D}").SendAsync("chatChannelChanged", item);
                foreach (var member in item.MemberUserIds) await hub.Clients.Group($"user:{member.ToLowerInvariant()}").SendAsync("chatChannelChanged", item);
                return Results.Ok(item);
            }
            catch (Exception ex) { return Error(ex); }
        });
        api.MapPost("/chat/channels/{id:guid}/archive", async (Guid id, ArchiveChatChannelRequest request, HttpContext context, FeatureStore store, IHubContext<OperationsHub> hub) =>
        {
            try
            {
                var item = store.SetChatChannelArchived(id, request.Archived, Actor(context));
                await hub.Clients.Group($"chat:{item.Id:D}").SendAsync("chatChannelChanged", item);
                return Results.Ok(item);
            }
            catch (Exception ex) { return Error(ex); }
        });
        api.MapGet("/chat/channels/{id:guid}/messages", (Guid id, DateTimeOffset? beforeUtc, int? limit, HttpContext context, FeatureStore store) =>
            Try(() => Results.Ok(store.GetChatHistory(id, Actor(context), beforeUtc, limit ?? 100))));
        api.MapPost("/chat/channels/{id:guid}/messages", async (Guid id, SendChatMessageRequest request, HttpContext context, FeatureStore store, IHubContext<OperationsHub> hub) =>
        {
            try
            {
                var item = store.SendChatMessage(id, request, Actor(context));
                await hub.Clients.Group($"chat:{item.ChannelId:D}").SendAsync("chatMessage", item);
                foreach (var mention in item.Mentions) await hub.Clients.Group($"user:{mention.ToLowerInvariant()}").SendAsync("chatMention", item);
                return Results.Ok(item);
            }
            catch (Exception ex) { return Error(ex); }
        });
        api.MapPost("/chat/channels/{id:guid}/read", async (Guid id, MarkChatReadRequest request, HttpContext context, FeatureStore store, IHubContext<OperationsHub> hub) =>
        {
            try
            {
                var state = store.MarkChatRead(id, request.ThroughMessageId, Actor(context));
                await hub.Clients.Group($"chat:{id:D}").SendAsync("chatReadState", state);
                return Results.Ok(state);
            }
            catch (Exception ex) { return Error(ex); }
        });
        api.MapGet("/chat/search", (Guid? agencyId, string q, int? limit, HttpContext context, FeatureStore store) => Try(() =>
        {
            var actor = Actor(context);
            var actorAgency = store.ResolveActorAgency(actor);
            return Results.Ok(store.SearchChat(agencyId ?? actorAgency, actor, q, limit ?? 100));
        }));

        api.MapGet("/bulletins", (FeatureStore store) => Results.Ok(store.Bulletins));
        api.MapPost("/bulletins", (CreateBulletinRequest request, FeatureStore store) => Try(() => Results.Ok(store.CreateBulletin(request))));

        api.MapGet("/station/deliveries", (Guid? stationId, FeatureStore store) => Results.Ok(stationId is Guid id ? store.StationDeliveries.Where(x => x.StationId == id).ToArray() : store.StationDeliveries));
        api.MapPost("/station/deliveries/{id:guid}/acknowledge", async (Guid id, StationAcknowledgeRequest request, HttpContext context, FeatureStore store, IHubContext<OperationsHub> hub) =>
        {
            try
            {
                var delivery = store.AcknowledgeStationDelivery(id, request with { Actor = Actor(context) });
                await hub.Clients.Group($"agency:{delivery.AgencyId:D}").SendAsync("stationDeliveryChanged", delivery);
                await hub.Clients.Group($"incident:{delivery.IncidentId:D}").SendAsync("stationDeliveryChanged", delivery);
                return Results.Ok(delivery);
            }
            catch (Exception ex) { return Error(ex); }
        });
        api.MapGet("/station/{stationId:guid}/snapshot", (Guid stationId, FeatureStore store) => Try(() => Results.Ok(store.GetStationSnapshot(stationId))));
        api.MapGet("/station/{stationId:guid}/delivery-paths", (Guid stationId, FeatureStore store) => Results.Ok(store.GetStationDeliveryPaths(stationId)));
        api.MapGet("/station/run-sheets", (Guid? stationId, FeatureStore store) => Results.Ok(stationId is Guid id ? store.RunSheets.Where(x => x.StationId == id).ToArray() : store.RunSheets));
        api.MapPost("/station/run-sheets/{id:guid}/printed", async (Guid id, NotesActionRequest request, HttpContext context, FeatureStore store, IHubContext<OperationsHub> hub) =>
        {
            try
            {
                var sheet = store.MarkRunSheetPrinted(id, Actor(context));
                await hub.Clients.Group($"agency:{sheet.AgencyId:D}").SendAsync("runSheetChanged", sheet);
                await hub.Clients.Group($"incident:{sheet.IncidentId:D}").SendAsync("runSheetChanged", sheet);
                return Results.Ok(sheet);
            }
            catch (Exception ex) { return Error(ex); }
        });
        api.MapPost("/station/run-sheets/{id:guid}/print", async (Guid id, string? printerKey, HttpContext context, FeatureStore store, ProviderRegistry registry, IHubContext<OperationsHub> hub, CancellationToken cancellationToken) =>
        {
            try
            {
                var sheet = store.RunSheets.FirstOrDefault(x => x.Id == id) ?? throw new KeyNotFoundException("Run sheet not found.");
                var providers = registry.GetAll<IPrintProvider>();
                if (providers.Count == 0) throw new InvalidOperationException("No print provider is configured.");
                var provider = providers[0];
                var bytes = System.Text.Encoding.UTF8.GetBytes(sheet.Content);
                var operation = new ProviderOperationContext(Guid.NewGuid(), sheet.AgencyId, sheet.IncidentId, Actor(context), DateTimeOffset.UtcNow, sheet.Id.ToString("N"));
                var result = await provider.PrintAsync(operation, new PrintJobRequest(string.IsNullOrWhiteSpace(printerKey) ? "default" : printerKey.Trim(), "RunSheet", sheet.IncidentId, sheet.Revision, bytes, "text/plain"), cancellationToken);
                RunSheetDto? updatedSheet = null;
                if (result.State is "Printed" or "Spooled" or "Confirmed") updatedSheet = store.MarkRunSheetPrinted(sheet.Id, Actor(context));
                if (updatedSheet is not null)
                {
                    await hub.Clients.Group($"agency:{sheet.AgencyId:D}").SendAsync("runSheetChanged", updatedSheet, cancellationToken);
                    await hub.Clients.Group($"incident:{sheet.IncidentId:D}").SendAsync("runSheetChanged", updatedSheet, cancellationToken);
                }
                return Results.Ok(new StationPrintResultDto(result.OperationId, sheet.Id, result.State, result.ProviderReference, result.Error, result.CompletedAtUtc));
            }
            catch (Exception ex) { return Error(ex); }
        });
        api.MapGet("/clients", (FeatureStore store) => Results.Ok(store.Heartbeats));
        api.MapPost("/clients/heartbeat", (HeartbeatRequest request, FeatureStore store) => Results.Ok(store.Heartbeat(request)));

        api.MapGet("/providers", (FeatureStore store) => Results.Ok(store.Providers));
        api.MapGet("/providers/health", (FeatureStore store) => Results.Ok(store.ProviderHealth));
        api.MapGet("/providers/runtime", async (ProviderRegistry registry, CancellationToken cancellationToken) =>
        {
            var rows = new List<object>();
            foreach (var provider in registry.Providers.OrderBy(x => x.Category).ThenBy(x => x.DisplayName))
            {
                var validation = await provider.ValidateConfigurationAsync(cancellationToken);
                var health = await provider.GetHealthAsync(cancellationToken);
                var capabilities = await provider.GetCapabilitiesAsync(cancellationToken);
                rows.Add(new { provider.Key, provider.DisplayName, provider.Category, validation, health, capabilities });
            }
            return Results.Ok(rows);
        });
        api.MapPut("/providers/{key}", (string key, UpsertProviderConfigRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.UpsertProvider(key, request, Actor(context)))));
        api.MapPost("/providers/{key}/validate", (string key, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.ValidateProvider(key, Actor(context)))));
        api.MapGet("/video/cameras", (FeatureStore store) => Results.Ok(store.Cameras));
        api.MapPost("/video/cameras", (UpsertCameraFeedRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.UpsertCamera(null, request, Actor(context)))));
        api.MapPut("/video/cameras/{id:guid}", (Guid id, UpsertCameraFeedRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.UpsertCamera(id, request, Actor(context)))));
        api.MapGet("/radio/streams", (FeatureStore store) => Results.Ok(store.RadioStreams));
        api.MapPost("/radio/streams", (UpsertRadioStreamRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.UpsertRadioStream(null, request, Actor(context)))));
        api.MapPut("/radio/streams/{id:guid}", (Guid id, UpsertRadioStreamRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.UpsertRadioStream(id, request, Actor(context)))));


        api.MapGet("/integrations/events", (Guid? agencyId, string? category, FeatureStore store, HttpContext context) => Try(() =>
        {
            var actorAgency = store.ResolveActorAgency(Actor(context));
            if (agencyId is Guid requested && requested != actorAgency) throw new UnauthorizedAccessException("Integration events are limited to the signed-in user's agency.");
            return Results.Ok(store.IntegrationEvents.Where(x => x.AgencyId == actorAgency && (string.IsNullOrWhiteSpace(category) || x.Category.Equals(category, StringComparison.OrdinalIgnoreCase))).Take(1000).ToArray());
        }));
        api.MapGet("/integrations/operations", (Guid? agencyId, FeatureStore store, HttpContext context) => Try(() =>
        {
            var actorAgency = store.ResolveActorAgency(Actor(context));
            if (agencyId is Guid requested && requested != actorAgency) throw new UnauthorizedAccessException("Integration operations are limited to the signed-in user's agency.");
            return Results.Ok(store.IntegrationOperations.Where(x => x.AgencyId == actorAgency).Take(1000).ToArray());
        }));
        api.MapGet("/integrations/operations/{id:guid}/attempts", (Guid id, FeatureStore store, HttpContext context) => Try(() =>
        {
            var operation = store.IntegrationOperations.FirstOrDefault(x => x.Id == id) ?? throw new KeyNotFoundException("Integration operation not found.");
            if (operation.AgencyId != store.ResolveActorAgency(Actor(context))) throw new UnauthorizedAccessException("Integration operation belongs to another agency.");
            return Results.Ok(store.IntegrationAttempts.Where(x => x.OperationId == id).OrderBy(x => x.AttemptNumber).ToArray());
        }));
        api.MapGet("/integrations/recordings", (Guid? incidentId, FeatureStore store, HttpContext context) => Try(() =>
        {
            var agency = store.ResolveActorAgency(Actor(context));
            return Results.Ok(store.RecordingReferences.Where(x => x.AgencyId == agency && (incidentId is null || x.IncidentId == incidentId)).ToArray());
        }));
        api.MapGet("/integrations/dashboard", (FeatureStore store, HttpContext context) => Try(() =>
        {
            var agency = store.ResolveActorAgency(Actor(context));
            return Results.Ok(new IntegrationDashboardDto(
                store.ProviderHealth,
                store.IntegrationEvents.Where(x => x.AgencyId == agency).Take(100).ToArray(),
                store.IntegrationOperations.Where(x => x.AgencyId == agency).Take(100).ToArray(),
                store.RecordingReferences.Where(x => x.AgencyId == agency).Take(100).ToArray(),
                store.Cameras.Where(x => x.AgencyId == agency).ToArray(),
                store.Vessels.Where(x => x.AgencyId == agency).ToArray()));
        }));
        api.MapPost("/integrations/911/events", (Ingest911GatewayEventRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.Ingest911GatewayEvent(request, Actor(context)))));
        api.MapPost("/integrations/radio/events", (IngestRadioGatewayEventRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.IngestRadioGatewayEvent(request, Actor(context)))));
        api.MapPost("/integrations/ais/events", (IngestAisGatewayEventRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.IngestAisGatewayEvent(request, Actor(context)))));
        api.MapPost("/integrations/routing/route", async (ProviderRouteRequest request, HttpContext context, FeatureStore store, ProviderRegistry registry, StatePersistenceService persistence, CancellationToken cancellationToken) =>
        {
            try
            {
                var actor = Actor(context);
                if (store.ResolveActorAgency(actor) != request.AgencyId) throw new UnauthorizedAccessException("Routing request belongs to another agency.");
                var provider = !string.IsNullOrWhiteSpace(request.ProviderKey) ? registry.Get<IRoutingProvider>(request.ProviderKey) : FirstOrNull(registry.GetAll<IRoutingProvider>());
                if (provider is null) throw new InvalidOperationException("No runtime routing provider is available.");
                var op = store.BeginIntegrationOperation(request.AgencyId, provider.Key, "Routing", request.IncidentId, request.UnitId, request, actor);
                var submitting = store.MarkIntegrationSubmitting(op.Id, actor);
                await PersistIntegrationBoundaryAsync(persistence, cancellationToken);
                QualificationFaultInjection.CrashIfRequested(context, "integration-after-submitting");
                var avoid = request.AvoidRestrictions ? store.CoreRoadRestrictionGeoJson() : [];
                var result = await provider.RouteAsync(new RouteProviderRequest(new RoutePoint(request.FromLatitude, request.FromLongitude), new RoutePoint(request.ToLatitude, request.ToLongitude), avoid, string.IsNullOrWhiteSpace(request.VehicleProfile) ? "Emergency" : request.VehicleProfile.Trim()), cancellationToken);
                _ = store.CompleteIntegrationAttempt(op.Id, submitting.Attempt.Id, "Confirmed", $"ROUTE-{provider.Key}-{submitting.Attempt.ProviderOperationId:N}", null, actor);
                return Results.Ok(new ProviderRouteResultDto(op.Id, provider.Key, result.DistanceMeters, result.DurationSeconds, result.GeometryGeoJson, result.Warnings, DateTimeOffset.UtcNow));
            }
            catch (Exception ex) { return Error(ex); }
        });
        api.MapPost("/integrations/recordings/query", async (RecordingLookupRequest request, HttpContext context, FeatureStore store, ProviderRegistry registry, StatePersistenceService persistence, CancellationToken cancellationToken) =>
        {
            try
            {
                var actor = Actor(context);
                if (store.ResolveActorAgency(actor) != request.AgencyId) throw new UnauthorizedAccessException("Recording query belongs to another agency.");
                var provider = !string.IsNullOrWhiteSpace(request.ProviderKey) ? registry.Get<IRecordingProvider>(request.ProviderKey) : FirstOrNull(registry.GetAll<IRecordingProvider>());
                if (provider is null) throw new InvalidOperationException("No runtime recording provider is available.");
                var op = store.BeginIntegrationOperation(request.AgencyId, provider.Key, "RecordingLookup", request.IncidentId, null, request, actor);
                var submitting = store.MarkIntegrationSubmitting(op.Id, actor);
                await PersistIntegrationBoundaryAsync(persistence, cancellationToken);
                QualificationFaultInjection.CrashIfRequested(context, "integration-after-submitting");
                var rows = await provider.FindRecordingsAsync(new RecordingQuery(request.IncidentId, request.Channel, request.TelephoneNumber, request.FromUtc, request.ToUtc), cancellationToken);
                var linked = rows.Select(x => store.AddRecordingReference(request.AgencyId, request.IncidentId, provider.Key, x.ProviderReference, x.MediaType, x.StartUtc, x.EndUtc, x.Description, x.PlaybackUri?.ToString(), actor)).ToArray();
                _ = store.CompleteIntegrationAttempt(op.Id, submitting.Attempt.Id, "Confirmed", $"LOOKUP-{provider.Key}-{submitting.Attempt.ProviderOperationId:N}", null, actor);
                return Results.Ok(linked);
            }
            catch (Exception ex) { return Error(ex); }
        });
        api.MapPost("/integrations/cameras/discover", async (CameraDiscoveryRequest request, HttpContext context, FeatureStore store, ProviderRegistry registry, StatePersistenceService persistence, CancellationToken cancellationToken) =>
        {
            try
            {
                var actor = Actor(context);
                if (store.ResolveActorAgency(actor) != request.AgencyId) throw new UnauthorizedAccessException("Camera discovery belongs to another agency.");
                var provider = !string.IsNullOrWhiteSpace(request.ProviderKey) ? registry.Get<ICameraDiscoveryProvider>(request.ProviderKey) : FirstOrNull(registry.GetAll<ICameraDiscoveryProvider>());
                if (provider is null) throw new InvalidOperationException("No runtime camera discovery provider is available.");
                var op = store.BeginIntegrationOperation(request.AgencyId, provider.Key, "CameraDiscovery", null, null, request, actor);
                var submitting = store.MarkIntegrationSubmitting(op.Id, actor);
                await PersistIntegrationBoundaryAsync(persistence, cancellationToken);
                QualificationFaultInjection.CrashIfRequested(context, "integration-after-submitting");
                var rows = await provider.DiscoverAsync(request.StationId, cancellationToken);
                _ = store.CompleteIntegrationAttempt(op.Id, submitting.Attempt.Id, "Confirmed", $"DISCOVERY-{provider.Key}-{submitting.Attempt.ProviderOperationId:N}", null, actor);
                return Results.Ok(rows.Select(x => new CameraDiscoveryCandidateDto(provider.Key, x.NativeId, x.Name, x.Uri, x.Manufacturer, x.Model, x.SerialNumber, x.State, DateTimeOffset.UtcNow)).ToArray());
            }
            catch (Exception ex) { return Error(ex); }
        });
        api.MapPost("/integrations/cameras/import", (ImportDiscoveredCameraRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.ImportDiscoveredCamera(request, Actor(context)))));
        api.MapPost("/integrations/radio/clear-emergency", async (RadioEmergencyClearRequest request, HttpContext context, FeatureStore store, ProviderRegistry registry, StatePersistenceService persistence, CancellationToken cancellationToken) =>
        {
            try
            {
                var actor = Actor(context);
                if (store.ResolveActorAgency(actor) != request.AgencyId) throw new UnauthorizedAccessException("Radio command belongs to another agency.");
                var provider = !string.IsNullOrWhiteSpace(request.ProviderKey) ? registry.Get<IRadioProvider>(request.ProviderKey) : FirstOrNull(registry.GetAll<IRadioProvider>());
                if (provider is null) throw new InvalidOperationException("No runtime radio provider is available.");
                var op = store.BeginIntegrationOperation(request.AgencyId, provider.Key, "RadioClearEmergency", request.IncidentId, request.UnitId, request, actor);
                var submitting = store.MarkIntegrationSubmitting(op.Id, actor);
                await PersistIntegrationBoundaryAsync(persistence, cancellationToken);
                QualificationFaultInjection.CrashIfRequested(context, "integration-after-submitting");
                var result = await provider.ClearEmergencyAsync(new ProviderOperationContext(submitting.Attempt.ProviderOperationId, request.AgencyId, request.IncidentId, actor, submitting.Attempt.StartedAtUtc, request.NativeRadioId), request.NativeRadioId, cancellationToken);
                return Results.Ok(store.CompleteIntegrationAttempt(op.Id, submitting.Attempt.Id, result.State, result.ProviderReference, result.Error, actor));
            }
            catch (Exception ex) { return Error(ex); }
        });
        api.MapPost("/integrations/operations/{id:guid}/retry", async (Guid id, RetryIntegrationOperationRequest request, HttpContext context, FeatureStore store, ProviderRegistry registry, StatePersistenceService persistence, CancellationToken cancellationToken) =>
        {
            try
            {
                var actor = Actor(context);
                var op = store.IntegrationOperations.FirstOrDefault(x => x.Id == id) ?? throw new KeyNotFoundException("Integration operation not found.");
                if (store.ResolveActorAgency(actor) != op.AgencyId) throw new UnauthorizedAccessException("Integration operation belongs to another agency.");
                if (op.State == "NeedsReconciliation" && !request.ConfirmPossibleDuplicate) throw new InvalidOperationException("Possible duplicate provider action must be explicitly acknowledged before retry.");
                if (op.State is not ("Failed" or "NeedsReconciliation")) throw new InvalidOperationException("Only Failed or NeedsReconciliation operations can be retried.");
                if (!op.Kind.Equals("RadioClearEmergency", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Automatic retry is only supported for side-effecting radio clear-emergency operations in Pass 24.");
                var payload = System.Text.Json.JsonSerializer.Deserialize<RadioEmergencyClearRequest>(op.RequestJson) ?? throw new InvalidOperationException("Stored integration request is invalid.");
                var provider = registry.Get<IRadioProvider>(op.ProviderKey) ?? throw new InvalidOperationException("Configured runtime radio provider is unavailable.");
                var submitting = store.MarkIntegrationSubmitting(op.Id, actor);
                await PersistIntegrationBoundaryAsync(persistence, cancellationToken);
                QualificationFaultInjection.CrashIfRequested(context, "integration-after-submitting");
                var result = await provider.ClearEmergencyAsync(new ProviderOperationContext(submitting.Attempt.ProviderOperationId, op.AgencyId, op.IncidentId, actor, submitting.Attempt.StartedAtUtc, payload.NativeRadioId), payload.NativeRadioId, cancellationToken);
                return Results.Ok(store.CompleteIntegrationAttempt(op.Id, submitting.Attempt.Id, result.State, result.ProviderReference, result.Error, actor));
            }
            catch (Exception ex) { return Error(ex); }
        });
        api.MapPost("/integrations/operations/{id:guid}/reconcile", (Guid id, ReconcileIntegrationOperationRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.ReconcileIntegrationOperation(id, request, Actor(context)))));

        api.MapGet("/workforce/leave", (FeatureStore store) => Results.Ok(store.LeaveRequests));
        api.MapPost("/workforce/leave", (CreateLeaveRequest request, FeatureStore store) => Try(() => Results.Ok(store.CreateLeave(request))));
        api.MapPost("/workforce/leave/{id:guid}/review", (Guid id, ReviewLeaveRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.ReviewLeave(id, request with { Actor = Actor(context) }))));
        api.MapGet("/workforce/trades", (FeatureStore store) => Results.Ok(store.ShiftTrades));
        api.MapPost("/workforce/trades", (CreateShiftTradeRequest request, FeatureStore store) => Try(() => Results.Ok(store.CreateShiftTrade(request))));
        api.MapPost("/workforce/trades/{id:guid}/review", (Guid id, ReviewShiftTradeRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.ReviewShiftTrade(id, request with { Actor = Actor(context) }))));
        api.MapGet("/workforce/staffing", (Guid? agencyId, DateOnly? date, FeatureStore store) => Results.Ok(store.Staffing(agencyId ?? OperationalStore.DefaultAgencyId, date ?? DateOnly.FromDateTime(DateTime.UtcNow))));
        api.MapGet("/workforce/rotations", (FeatureStore store) => Results.Ok(store.Rotations));
        api.MapPost("/workforce/rotations", (UpsertRotationRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.UpsertRotation(null, request with { Actor = Actor(context) }))));
        api.MapPut("/workforce/rotations/{id:guid}", (Guid id, UpsertRotationRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.UpsertRotation(id, request with { Actor = Actor(context) }))));
        api.MapGet("/workforce/overtime", (FeatureStore store) => Results.Ok(store.OvertimeOffers));
        api.MapPost("/workforce/overtime", (CreateOvertimeOfferRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.CreateOvertimeOffer(request, Actor(context)))));
        api.MapPost("/workforce/overtime/{id:guid}/accept", (Guid id, AcceptOvertimeRequest request, FeatureStore store) => Try(() => Results.Ok(store.AcceptOvertime(id, request))));
        api.MapGet("/workforce/timesheets", (FeatureStore store) => Results.Ok(store.Timesheets));
        api.MapPost("/workforce/timesheets", (UpsertTimesheetEntryRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.UpsertTimesheet(null, request, Actor(context)))));
        api.MapPut("/workforce/timesheets/{id:guid}", (Guid id, UpsertTimesheetEntryRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.UpsertTimesheet(id, request, Actor(context)))));
        api.MapPost("/workforce/timesheets/{id:guid}/approve", (Guid id, ApproveTimesheetRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.ApproveTimesheet(id, request with { Actor = Actor(context) }))));
        api.MapGet("/workforce/payroll-export", (Guid? agencyId, DateOnly from, DateOnly to, FeatureStore store) => Try(() => Results.Ok(store.PayrollExport(agencyId ?? OperationalStore.DefaultAgencyId, from, to))));
        api.MapGet("/workforce/roster", (Guid? agencyId, DateOnly? date, FeatureStore store) => Results.Ok(store.DailyRoster(agencyId ?? OperationalStore.DefaultAgencyId, date ?? DateOnly.FromDateTime(DateTime.UtcNow))));
        api.MapGet("/workforce/staffing-rules", (Guid? agencyId, FeatureStore store) => Results.Ok(store.StaffingRules.Where(x => agencyId is null || x.AgencyId == agencyId).ToArray()));
        api.MapPost("/workforce/staffing-rules", (UpsertStaffingRuleRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.UpsertStaffingRule(null, request, Actor(context)))));
        api.MapPut("/workforce/staffing-rules/{id:guid}", (Guid id, UpsertStaffingRuleRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.UpsertStaffingRule(id, request, Actor(context)))));
        api.MapGet("/workforce/assignments", (Guid? agencyId, DateOnly? date, FeatureStore store) => Results.Ok(store.WorkforceAssignments.Where(x => (agencyId is null || x.AgencyId == agencyId) && (date is null || x.Date == date)).ToArray()));
        api.MapPost("/workforce/assignments", (UpsertWorkforceAssignmentRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.UpsertWorkforceAssignment(null, request, Actor(context)))));
        api.MapPut("/workforce/assignments/{id:guid}", (Guid id, UpsertWorkforceAssignmentRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.UpsertWorkforceAssignment(id, request, Actor(context)))));
        api.MapPost("/workforce/assignments/{id:guid}/call-out", (Guid id, WorkforceActionRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.CallOutWorkforceAssignment(id, request, Actor(context)))));
        api.MapPost("/workforce/assignments/{id:guid}/relieve", (Guid id, WorkforceReliefRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.RelieveWorkforceAssignment(id, request, Actor(context)))));
        api.MapGet("/workforce/staffing-evaluation", (Guid? agencyId, DateOnly? date, FeatureStore store) => Results.Ok(store.EvaluateStaffing(agencyId ?? OperationalStore.DefaultAgencyId, date ?? DateOnly.FromDateTime(DateTime.UtcNow))));
        api.MapGet("/workforce/roster/current", (Guid? agencyId, DateOnly? date, FeatureStore store) => Results.Ok(store.GetWorkforceRoster(agencyId ?? OperationalStore.DefaultAgencyId, date ?? DateOnly.FromDateTime(DateTime.UtcNow))));
        api.MapGet("/workforce/roster/snapshots", (Guid? agencyId, DateOnly? date, FeatureStore store) => Results.Ok(store.WorkforceRosterSnapshots.Where(x => (agencyId is null || x.AgencyId == agencyId) && (date is null || x.Date == date)).ToArray()));
        api.MapPost("/workforce/roster/publish", (Guid? agencyId, DateOnly? date, PublishWorkforceRosterRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.PublishWorkforceRoster(agencyId ?? OperationalStore.DefaultAgencyId, date ?? DateOnly.FromDateTime(DateTime.UtcNow), request, Actor(context)))));
        api.MapGet("/workforce/roster/print-view", (Guid? agencyId, DateOnly? date, bool includeChanges, FeatureStore store) => Results.Ok(store.BuildWorkforceRosterPrint(agencyId ?? OperationalStore.DefaultAgencyId, date ?? DateOnly.FromDateTime(DateTime.UtcNow), includeChanges)));
        api.MapPost("/workforce/roster/print", async (Guid? agencyId, DateOnly? date, bool includeChanges, string? printerKey, HttpContext context, FeatureStore store, ProviderRegistry registry, CancellationToken cancellationToken) =>
        {
            try
            {
                var roster = store.BuildWorkforceRosterPrint(agencyId ?? OperationalStore.DefaultAgencyId, date ?? DateOnly.FromDateTime(DateTime.UtcNow), includeChanges);
                var providers = registry.GetAll<IPrintProvider>();
                if (providers.Count == 0) throw new InvalidOperationException("No print provider is configured.");
                var operationId = Guid.NewGuid();
                var operation = new ProviderOperationContext(operationId, roster.AgencyId, Guid.Empty, Actor(context), DateTimeOffset.UtcNow, $"workforce-roster:{roster.Date:yyyyMMdd}:{roster.ContentSha256}");
                var result = await providers[0].PrintAsync(operation, new PrintJobRequest(string.IsNullOrWhiteSpace(printerKey) ? "default" : printerKey.Trim(), "WorkforceRoster", Guid.Empty, 1, System.Text.Encoding.UTF8.GetBytes(roster.Content), "text/plain"), cancellationToken);
                return Results.Ok(new WorkforceRosterPrintResultDto(result.OperationId, roster.AgencyId, roster.Date, result.State, result.ProviderReference, result.Error, result.CompletedAtUtc));
            }
            catch (Exception ex) { return Error(ex); }
        });

        api.MapGet("/documents/{id:guid}/versions", (Guid id, FeatureStore store) => Results.Ok(store.GetDocumentVersions(id)));
        api.MapPost("/documents/{id:guid}/versions", (Guid id, NotesActionRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.AddDocumentVersion(id, request.Notes ?? string.Empty, Actor(context)))));
        api.MapGet("/documents/{id:guid}/acknowledgements", (Guid id, FeatureStore store) => Results.Ok(store.GetDocumentAcknowledgements(id)));
        api.MapPost("/documents/{id:guid}/acknowledgements", (Guid id, AssignAcknowledgementRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.AssignDocumentAcknowledgements(id, request with { Actor = Actor(context) }))));
        api.MapPost("/documents/acknowledgements/{id:guid}/acknowledge", (Guid id, AcknowledgeDocumentRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.AcknowledgeDocument(id, request with { Recipient = Actor(context) }))));

        api.MapGet("/documents/dms", (Guid? agencyId, FeatureStore store) => Results.Ok(store.GetDmsDocuments(agencyId)));
        api.MapPost("/documents/dms", (CreateDmsDocumentRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.CreateDmsDocument(request, Actor(context)))));
        api.MapGet("/documents/dms/{id:guid}/revisions", (Guid id, FeatureStore store) => Try(() => Results.Ok(store.GetDmsRevisions(id))));
        api.MapPost("/documents/dms/{id:guid}/revisions", (Guid id, CreateDmsRevisionRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.CreateDmsRevision(id, request, Actor(context)))));
        api.MapPost("/documents/dms/{id:guid}/revisions/{version:int}/approval", (Guid id, int version, DmsApprovalRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.StartDmsRevisionApproval(id, version, request, Actor(context)))));
        api.MapPost("/documents/dms/{id:guid}/revisions/{version:int}/publish", (Guid id, int version, PublishDmsRevisionRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.PublishDmsRevision(id, version, request, Actor(context)))));
        api.MapGet("/documents/dms/{id:guid}/distributions", (Guid id, FeatureStore store) => Try(() => Results.Ok(store.GetDmsDistributions(id))));
        api.MapPost("/documents/dms/{id:guid}/distributions", (Guid id, CreateDmsDistributionRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.CreateDmsDistribution(id, request, Actor(context)))));
        api.MapGet("/documents/dms/{id:guid}/read-receipts", (Guid id, FeatureStore store) => Try(() => Results.Ok(store.GetDmsReadReceipts(id))));
        api.MapGet("/documents/dms/{id:guid}/revisions/{version:int}/required-read", (Guid id, int version, FeatureStore store) => Try(() => Results.Ok(store.GetDmsRequiredReadStatus(id, version))));
        api.MapPost("/documents/dms/read-receipts/{id:guid}/read", (Guid id, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.MarkDmsRead(id, Actor(context)))));
        api.MapPost("/documents/dms/read-receipts/{id:guid}/acknowledge", (Guid id, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.AcknowledgeDmsRead(id, Actor(context)))));
        api.MapGet("/documents/dms/{id:guid}/assets", (Guid id, FeatureStore store) => Try(() => Results.Ok(store.GetDmsAssetReferences(id))));
        api.MapPost("/documents/dms/{id:guid}/assets", (Guid id, AddDmsAssetReferenceRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.AddDmsAssetReference(id, request, Actor(context)))));
        api.MapGet("/documents/dms/{id:guid}/links", (Guid id, FeatureStore store) => Try(() => Results.Ok(store.GetDmsDocumentLinks(id))));
        api.MapPost("/documents/dms/{id:guid}/links", (Guid id, CreateDmsDocumentLinkRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.LinkDmsDocument(id, request, Actor(context)))));
        api.MapGet("/documents/dms/entity-links", (string entityType, string entityKey, FeatureStore store) => Try(() => Results.Ok(store.GetDmsEntityLinks(entityType, entityKey))));

        api.MapGet("/forms/templates", (FeatureStore store) => Results.Ok(store.FormTemplates));
        api.MapPost("/forms/templates", (CreateFormTemplateRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.CreateFormTemplate(request with { CreatedBy = Actor(context) }))));
        api.MapPost("/forms/templates/{id:guid}/publish", (Guid id, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.PublishFormTemplate(id, Actor(context)))));
        api.MapGet("/forms", (FeatureStore store) => Results.Ok(store.Forms));
        api.MapPost("/forms", (CreateFormInstanceRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.CreateForm(request with { CreatedBy = Actor(context) }))));
        api.MapPost("/forms/{id:guid}/sign", (Guid id, SignFormRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.SignForm(id, request with { Actor = Actor(context) }))));
        api.MapPost("/forms/{id:guid}/sign-strong", (Guid id, SignFormStrongRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.SignFormStrong(id, request, Actor(context), "authenticated-api"))));
        api.MapGet("/forms/{id:guid}/signatures", (Guid id, FeatureStore store) => Results.Ok(store.GetFormSignatures(id)));
        api.MapGet("/documents/workflows", (Guid? entityId, FeatureStore store) => Results.Ok(entityId is Guid eid ? store.ApprovalWorkflows.Where(x => x.EntityId == eid).ToArray() : store.ApprovalWorkflows));
        api.MapPost("/documents/workflows", (CreateApprovalWorkflowRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.CreateApprovalWorkflow(request, Actor(context)))));
        api.MapGet("/documents/workflows/{id:guid}/steps", (Guid id, FeatureStore store) => Results.Ok(store.ApprovalSteps.Where(x => x.WorkflowId == id).OrderBy(x => x.Sequence).ToArray()));
        api.MapPost("/documents/workflow-steps/{id:guid}/decide", (Guid id, DecideApprovalStepRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.DecideApprovalStep(id, request, Actor(context)))));

        api.MapGet("/rms/inspections", (FeatureStore store) => Results.Ok(store.Inspections));
        api.MapPost("/rms/inspections", (CreateInspectionRequest request, FeatureStore store) => Try(() => Results.Ok(store.CreateInspection(request))));
        api.MapPost("/rms/inspections/{id:guid}/close", (Guid id, NotesActionRequest request, FeatureStore store) => Try(() => Results.Ok(store.CloseInspection(id, request.Actor))));
        api.MapGet("/rms/permits", (FeatureStore store) => Results.Ok(store.Permits));
        api.MapPost("/rms/permits", (CreatePermitRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.CreatePermit(request, Actor(context)))));
        api.MapGet("/rms/maintenance", (FeatureStore store) => Results.Ok(store.Maintenance));
        api.MapPost("/rms/maintenance", (CreateMaintenanceRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.CreateMaintenance(request, Actor(context)))));
        api.MapPost("/rms/maintenance/{id:guid}/complete", (Guid id, NotesActionRequest request, FeatureStore store) => Try(() => Results.Ok(store.CompleteMaintenance(id, request.Actor))));
        api.MapGet("/rms/neris", (FeatureStore store) => Results.Ok(store.NerisReports));
        api.MapPost("/rms/neris", (UpsertNerisReportRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.UpsertNeris(null, request, Actor(context)))));
        api.MapPut("/rms/neris/{id:guid}", (Guid id, UpsertNerisReportRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.UpsertNeris(id, request, Actor(context)))));
        api.MapPost("/rms/neris/{id:guid}/submit", (Guid id, SubmitNerisReportRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.SubmitNeris(id, request with { Actor = Actor(context) }))));
        api.MapPost("/rms/neris/{id:guid}/transport", async (Guid id, HttpContext context, FeatureStore store, ProviderRegistry registry, CancellationToken cancellationToken) =>
        {
            try
            {
                var report = store.NerisReports.FirstOrDefault(x => x.Id == id) ?? throw new KeyNotFoundException("NERIS report not found.");
                var providers = registry.GetAll<IFireReportingProvider>();
                if (providers.Count == 0) throw new InvalidOperationException("No fire-reporting transport is configured.");
                var provider = providers[0];
                var submission = new FireReportSubmission(report.Id, report.PayloadJson, "configured");
                var validation = await provider.ValidateReportAsync(submission, cancellationToken);
                if (!validation.Valid) return Results.BadRequest(new { error = "Provider validation failed.", validation.Errors, validation.Warnings });
                var operation = new ProviderOperationContext(Guid.NewGuid(), report.AgencyId, report.IncidentId, Actor(context), DateTimeOffset.UtcNow, report.Id.ToString("N"));
                var result = await provider.SubmitReportAsync(operation, submission, cancellationToken);
                if (result.State is "Accepted" or "Confirmed" && !string.IsNullOrWhiteSpace(result.ProviderReference)) store.CompleteNerisTransport(id, result.ProviderReference, Actor(context));
                return Results.Ok(result);
            }
            catch (Exception ex) { return Error(ex); }
        });
        api.MapGet("/rms/violations", (Guid? premiseId, FeatureStore store) => Results.Ok(premiseId is Guid pid ? store.Violations.Where(x => x.PremiseId == pid).ToArray() : store.Violations));
        api.MapPost("/rms/violations", (CreateViolationRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.CreateViolation(request, Actor(context)))));
        api.MapPost("/rms/violations/{id:guid}/resolve", (Guid id, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.ResolveViolation(id, Actor(context)))));
        api.MapGet("/rms/hazmat", (Guid? premiseId, FeatureStore store) => Results.Ok(premiseId is Guid pid ? store.Hazmat.Where(x => x.PremiseId == pid).ToArray() : store.Hazmat));
        api.MapPost("/rms/hazmat", (UpsertHazmatRecordRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.UpsertHazmat(null, request, Actor(context)))));
        api.MapPut("/rms/hazmat/{id:guid}", (Guid id, UpsertHazmatRecordRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.UpsertHazmat(id, request, Actor(context)))));
        api.MapGet("/rms/investigations", (Guid? incidentId, FeatureStore store) => Results.Ok(incidentId is Guid iid ? store.Investigations.Where(x => x.IncidentId == iid).ToArray() : store.Investigations));
        api.MapPost("/rms/investigations", (CreateInvestigationRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.CreateInvestigation(request, Actor(context)))));
        api.MapPost("/rms/investigations/{id:guid}/close", (Guid id, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.CloseInvestigation(id, Actor(context)))));
        api.MapGet("/rms/qualifications", (string? personId, FeatureStore store) => Results.Ok(string.IsNullOrWhiteSpace(personId) ? store.Qualifications : store.Qualifications.Where(x => x.PersonId.Equals(personId, StringComparison.OrdinalIgnoreCase)).ToArray()));
        api.MapPost("/rms/qualifications", (UpsertPersonnelQualificationRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.UpsertQualification(null, request, Actor(context)))));
        api.MapPut("/rms/qualifications/{id:guid}", (Guid id, UpsertPersonnelQualificationRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.UpsertQualification(id, request, Actor(context)))));

        api.MapPost("/me/totp/begin", (HttpContext context, MfaService mfa) => Try(() =>
        {
            var userId = Actor(context);
            var displayName = context.User.Identity?.Name ?? userId;
            return Results.Ok(mfa.BeginEnrollment(userId, displayName, userId));
        }));
        api.MapPost("/me/totp/confirm", (ConfirmTotpEnrollmentRequest request, HttpContext context, MfaService mfa) => Try(() => Results.Ok(mfa.ConfirmEnrollment(Actor(context), request with { Actor = Actor(context) }))));
        api.MapPost("/me/totp/verify", (VerifyTotpRequest request, HttpContext context, MfaService mfa, IConfiguration configuration) => Try(() =>
        {
            var actor = Actor(context);
            var verified = mfa.Verify(actor, request, actor);
            if (!verified.Success) return Results.Unauthorized();
            var session = mfa.IssueSession(actor, configuration.GetValue("Security:MfaSessionMinutes", 30));
            context.Response.Cookies.Append(MfaSessionMiddleware.CookieName, session.Token, new CookieOptions
            {
                HttpOnly = true,
                Secure = !context.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment(),
                SameSite = SameSiteMode.Strict,
                Expires = session.ExpiresAtUtc,
                IsEssential = true,
                Path = "/"
            });
            return Results.Ok(new { verified.Success, verified.UsedRecoveryCode, verified.VerifiedAtUtc, session.ExpiresAtUtc });
        }));
        api.MapGet("/me/totp/session", (HttpContext context, MfaService mfa) =>
        {
            var actor = Actor(context);
            if (!context.Request.Cookies.TryGetValue(MfaSessionMiddleware.CookieName, out var token)) return Results.Ok(new MfaSessionValidationDto(false, actor, null, "No MFA session."));
            return Results.Ok(mfa.VerifySession(token, actor));
        });
        api.MapPost("/me/totp/logout", (HttpContext context) =>
        {
            context.Response.Cookies.Delete(MfaSessionMiddleware.CookieName, new CookieOptions { Path = "/" });
            return Results.NoContent();
        });
        api.MapPost("/me/totp/disable", (DisableTotpRequest request, HttpContext context, MfaService mfa) => Try(() => { mfa.Disable(Actor(context), request with { Actor = Actor(context) }); return Results.NoContent(); }));
        api.MapPost("/me/offline-lease", (IssueOfflineAuthorizationLeaseRequest request, HttpContext context, MfaService mfa) => Try(() =>
        {
            var actor = Actor(context);
            if (!actor.Equals("local-development", StringComparison.OrdinalIgnoreCase) && !request.UserId.Equals(actor, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Offline authorization leases may only be issued for the current user.");
            return Results.Ok(mfa.IssueLease(request with { UserId = actor }));
        }));
        api.MapPost("/me/offline-lease/verify", (VerifyOfflineAuthorizationLeaseRequest request, MfaService mfa) => Results.Ok(mfa.VerifyLease(request)));

        api.MapGet("/security/devices", (FeatureStore store) => Results.Ok(store.Devices));
        api.MapPost("/security/devices", (RegisterDeviceRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.RegisterDevice(request, Actor(context)))));
        api.MapPost("/security/devices/{deviceId}/revoke", (string deviceId, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.RevokeDevice(deviceId, Actor(context)))));
        api.MapGet("/security/users", (FeatureStore store) => Results.Ok(store.Users));
        api.MapPut("/security/users/{userId}", (string userId, UpdateUserSecurityRequest request, HttpContext context, FeatureStore store) => Try(() => Results.Ok(store.UpsertUser(userId, request, Actor(context)))));
        api.MapGet("/audit", (string? actor, string? entityType, FeatureStore store) => Results.Ok(store.Audit.Where(x => (string.IsNullOrWhiteSpace(actor) || x.Actor.Contains(actor, StringComparison.OrdinalIgnoreCase)) && (string.IsNullOrWhiteSpace(entityType) || x.EntityType.Equals(entityType, StringComparison.OrdinalIgnoreCase))).Take(1000).ToArray()));
        api.MapGet("/audit/integrity", (FeatureStore store) => Results.Ok(store.AuditIntegrity));
        api.MapGet("/audit/verify", (FeatureStore store) => Results.Ok(store.VerifyAuditIntegrity()));

        api.MapGet("/admin/config", (string? area, FeatureStore store) => Results.Ok(string.IsNullOrWhiteSpace(area) ? store.ConfigVersions : store.ConfigVersions.Where(x => x.Area.Equals(area, StringComparison.OrdinalIgnoreCase)).ToArray()));
        api.MapPost("/admin/config", (CreateConfigDraftRequest request, FeatureStore store) => Try(() => Results.Ok(store.CreateConfigDraft(request))));
        api.MapPost("/admin/config/{id:guid}/publish", (Guid id, PublishConfigRequest request, FeatureStore store) => Try(() => Results.Ok(store.PublishConfig(id, request))));
        api.MapGet("/admin/ha", async (FeatureStore store, HaCoordinatorService ha, CancellationToken cancellationToken) =>
            Results.Ok(await ha.GetClusterStatusAsync(store, cancellationToken)));
        api.MapGet("/admin/ha/events", async (int? limit, HaCoordinatorService ha, CancellationToken cancellationToken) =>
            Results.Ok(await ha.GetClusterEventsAsync(limit ?? 100, cancellationToken)));
        api.MapPost("/admin/ha/local/drain", (HaDrainRequest request, HttpContext context, HaCoordinatorService ha, CancellationToken cancellationToken) =>
            TryAsync(async () => Results.Ok(await ha.DrainAsync(request, Actor(context), cancellationToken))));
        api.MapPost("/admin/ha/local/resume", (HaResumeRequest request, HttpContext context, HaCoordinatorService ha, CancellationToken cancellationToken) =>
            TryAsync(async () => Results.Ok(await ha.ResumeAsync(request, Actor(context), cancellationToken))));
        api.MapGet("/admin/operations", async (OperationalStore core, FeatureStore store, HaCoordinatorService ha, CancellationToken cancellationToken) => Results.Ok(new
        {
            asOfUtc = DateTimeOffset.UtcNow,
            activeIncidents = core.GetIncidents(openOnly: true).Count,
            units = core.Units.Count,
            activeAlerts = store.Alerts.Count(x => x.State != "Resolved"),
            connectedClients = store.Heartbeats.Count(x => x.State is "Connected" or "ClockWarning"),
            providerHealth = store.ProviderHealth,
            ha = await ha.GetClusterStatusAsync(store, cancellationToken)
        }));

        api.MapPost("/mobile/sync", (MobileSyncRequest request, HttpContext context, FeatureStore store, MfaService mfa) => Try(() =>
        {
            OfflineAuthorizationLeaseValidationDto? lease = null;
            if (!string.IsNullOrWhiteSpace(request.OfflineAuthorizationLease))
                lease = mfa.VerifyLease(new VerifyOfflineAuthorizationLeaseRequest(request.OfflineAuthorizationLease, request.DeviceId));
            return Results.Ok(store.MobileSync(request, Actor(context), lease));
        }));
        api.MapGet("/search/extended", (string q, FeatureStore store) => Results.Ok(store.ExtendedSearch(q)));

        return api;
    }

    private static async Task PersistIntegrationBoundaryAsync(StatePersistenceService persistence, CancellationToken cancellationToken)
    {
        if (persistence.Provider.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase)) await persistence.FlushWithinCommandGateAsync(cancellationToken);
        else await persistence.FlushAsync(cancellationToken);
        if (!persistence.Status.State.Equals("Healthy", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("DISRoute could not durably persist the provider-operation boundary: " + (persistence.Status.LastError ?? "unknown persistence error"));
    }

    private static T? FirstOrNull<T>(IReadOnlyList<T> items) where T : class => items.Count == 0 ? null : items[0];

    private static string Actor(HttpContext context) => context.User.Identity?.Name ?? "local-development";

    private static IResult Try(Func<IResult> action)
    {
        try { return action(); }
        catch (Exception ex) { return Error(ex); }
    }

    private static async Task<IResult> TryAsync(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (Exception ex) { return Error(ex); }
    }

    private static IResult Error(Exception ex) => ex switch
    {
        KeyNotFoundException => Results.NotFound(new { error = ex.Message }),
        ArgumentException => Results.BadRequest(new { error = ex.Message }),
        InvalidOperationException when ex.Message.StartsWith("Concurrency conflict", StringComparison.Ordinal) => Results.Conflict(new { error = ex.Message }),
        InvalidOperationException => Results.BadRequest(new { error = ex.Message }),
        UnauthorizedAccessException => Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status403Forbidden),
        _ => Results.Problem(title: "DISRoute operation failed", detail: ex.Message)
    };
}
