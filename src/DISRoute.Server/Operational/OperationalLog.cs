using Microsoft.Extensions.Logging;

namespace DISRoute.Server.Operational;

internal static class OperationalLog
{
    private static readonly Action<ILogger, Exception?> AlertEscalationWorkerFailedMessage =
        LoggerMessage.Define(LogLevel.Error, new EventId(1001, nameof(AlertEscalationWorkerFailed)), "Alert escalation worker iteration failed.");
    private static readonly Action<ILogger, Exception?> DispatchDeliveryWorkerFailedMessage =
        LoggerMessage.Define(LogLevel.Error, new EventId(1002, nameof(DispatchDeliveryWorkerFailed)), "Dispatch delivery worker iteration failed.");
    private static readonly Action<ILogger, Guid, string, Exception?> MutationPersistFailedMessage =
        LoggerMessage.Define<Guid, string>(LogLevel.Error, new EventId(1003, nameof(MutationPersistFailed)), "Failed to persist operational mutation {EventId} {EventType}; retrying same event.");
    private static readonly Action<ILogger, Exception?> HaLeaseLostMessage =
        LoggerMessage.Define(LogLevel.Error, new EventId(1004, nameof(HaLeaseLost)), "DISRoute HA coordinator lost its PostgreSQL lease session; node is fenced from writes.");
    private static readonly Action<ILogger, Exception?> OutboxPublishFailedMessage =
        LoggerMessage.Define(LogLevel.Error, new EventId(1005, nameof(OutboxPublishFailed)), "PostgreSQL outbox publish cycle failed.");
    private static readonly Action<ILogger, Exception?> RealtimeRelayDisconnectedMessage =
        LoggerMessage.Define(LogLevel.Error, new EventId(1006, nameof(RealtimeRelayDisconnected)), "PostgreSQL realtime relay disconnected; reconnecting.");
    private static readonly Action<ILogger, Exception?> RealtimeRelayNotificationFailedMessage =
        LoggerMessage.Define(LogLevel.Error, new EventId(1007, nameof(RealtimeRelayNotificationFailed)), "Failed to relay PostgreSQL realtime notification.");
    private static readonly Action<ILogger, string, DateTimeOffset, Exception?> StateLoadedMessage =
        LoggerMessage.Define<string, DateTimeOffset>(LogLevel.Information, new EventId(1008, nameof(StateLoaded)), "Loaded DISRoute runtime state from {Location} saved at {SavedAt}.");
    private static readonly Action<ILogger, Exception?> StateLoadFailedMessage =
        LoggerMessage.Define(LogLevel.Error, new EventId(1009, nameof(StateLoadFailed)), "Could not load DISRoute runtime state; seeded state remains active.");
    private static readonly Action<ILogger, Exception?> StateCheckpointFailedMessage =
        LoggerMessage.Define(LogLevel.Error, new EventId(1010, nameof(StateCheckpointFailed)), "Could not checkpoint DISRoute runtime state.");
    private static readonly Action<ILogger, int, long, long, long, string, Exception?> DurabilityTimeoutMessage =
        LoggerMessage.Define<int, long, long, long, string>(LogLevel.Error, new EventId(1011, nameof(DurabilityTimeout)), "Durability acknowledgement timed out after {TimeoutSeconds}s. queued={Queued} persisted={Persisted} failedAttempts={Failed} writer={WriterState}");
    private static readonly Action<ILogger, string, string, Exception?> DurabilityFailedMessage =
        LoggerMessage.Define<string, string>(LogLevel.Error, new EventId(1012, nameof(DurabilityFailed)), "Durability acknowledgement failed for {Method} {Path}.");
    private static readonly Action<ILogger, string, string, Exception?> PendingRestoreAppliedMessage =
        LoggerMessage.Define<string, string>(LogLevel.Warning, new EventId(1013, nameof(PendingRestoreApplied)), "Applied pending DISRoute file restore to {Location}; previous checkpoint preserved at {RollbackLocation}.");
    private static readonly Action<ILogger, string, Exception?> PortableBackupCreatedMessage =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(1014, nameof(PortableBackupCreated)), "Created and verified DISRoute portable backup {FileName}.");
    private static readonly Action<ILogger, string, Exception?> PortableBackupStagedMessage =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(1015, nameof(PortableBackupStaged)), "Staged DISRoute portable backup {FileName} for restore on next server restart.");
    private static readonly Action<ILogger, string, long, Exception?> HaLeaseAcquiredMessage =
        LoggerMessage.Define<string, long>(LogLevel.Warning, new EventId(1016, nameof(HaLeaseAcquired)), "DISRoute HA leadership acquired by {NodeId}; lease epoch {LeaseEpoch}.");
    private static readonly Action<ILogger, string, string, Exception?> HaLeaseReleasedMessage =
        LoggerMessage.Define<string, string>(LogLevel.Warning, new EventId(1017, nameof(HaLeaseReleased)), "DISRoute HA leadership released by {NodeId}; reason {Reason}.");
    private static readonly Action<ILogger, Exception?> NerisSubmissionWorkerFailedMessage =
        LoggerMessage.Define(LogLevel.Error, new EventId(1018, nameof(NerisSubmissionWorkerFailed)), "NERIS submission worker iteration failed.");

    public static void AlertEscalationWorkerFailed(ILogger logger, Exception exception) => AlertEscalationWorkerFailedMessage(logger, exception);
    public static void DispatchDeliveryWorkerFailed(ILogger logger, Exception exception) => DispatchDeliveryWorkerFailedMessage(logger, exception);
    public static void MutationPersistFailed(ILogger logger, Guid eventId, string eventType, Exception exception) => MutationPersistFailedMessage(logger, eventId, eventType, exception);
    public static void HaLeaseLost(ILogger logger, Exception exception) => HaLeaseLostMessage(logger, exception);
    public static void OutboxPublishFailed(ILogger logger, Exception exception) => OutboxPublishFailedMessage(logger, exception);
    public static void RealtimeRelayDisconnected(ILogger logger, Exception exception) => RealtimeRelayDisconnectedMessage(logger, exception);
    public static void RealtimeRelayNotificationFailed(ILogger logger, Exception exception) => RealtimeRelayNotificationFailedMessage(logger, exception);
    public static void StateLoaded(ILogger logger, string location, DateTimeOffset savedAt) => StateLoadedMessage(logger, location, savedAt, null);
    public static void StateLoadFailed(ILogger logger, Exception exception) => StateLoadFailedMessage(logger, exception);
    public static void StateCheckpointFailed(ILogger logger, Exception exception) => StateCheckpointFailedMessage(logger, exception);
    public static void DurabilityTimeout(ILogger logger, int timeoutSeconds, long queued, long persisted, long failed, string writerState) =>
        DurabilityTimeoutMessage(logger, timeoutSeconds, queued, persisted, failed, writerState, null);
    public static void DurabilityFailed(ILogger logger, string method, string path, Exception exception) =>
        DurabilityFailedMessage(logger, method, path, exception);
    public static void PendingRestoreApplied(ILogger logger, string location, string rollbackLocation) =>
        PendingRestoreAppliedMessage(logger, location, rollbackLocation, null);
    public static void PortableBackupCreated(ILogger logger, string fileName) =>
        PortableBackupCreatedMessage(logger, fileName, null);
    public static void PortableBackupStaged(ILogger logger, string fileName) =>
        PortableBackupStagedMessage(logger, fileName, null);
    public static void HaLeaseAcquired(ILogger logger, string nodeId, long leaseEpoch) =>
        HaLeaseAcquiredMessage(logger, nodeId, leaseEpoch, null);
    public static void HaLeaseReleased(ILogger logger, string nodeId, string reason) =>
        HaLeaseReleasedMessage(logger, nodeId, reason, null);
    public static void NerisSubmissionWorkerFailed(ILogger logger, Exception exception) =>
        NerisSubmissionWorkerFailedMessage(logger, exception);
}
