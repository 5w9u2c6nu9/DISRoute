using System.Diagnostics;
using DISRoute.Application.Abstractions;

namespace DISRoute.Data;

public sealed class LoggingAuditSink : IAuditSink
{
    public Task WriteAsync(AuditRecord record, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Trace.WriteLine(
            $"AUDIT actor={record.Actor} action={record.Action} target={record.Target} allowed={record.Allowed} correlation={record.CorrelationId}");
        return Task.CompletedTask;
    }
}
