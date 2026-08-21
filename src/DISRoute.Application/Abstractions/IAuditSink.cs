namespace DISRoute.Application.Abstractions;
public interface IAuditSink { Task WriteAsync(AuditRecord record, CancellationToken ct=default); }
public sealed record AuditRecord(DateTimeOffset AtUtc,string Actor,string Action,string? Target,bool Allowed,string CorrelationId);
