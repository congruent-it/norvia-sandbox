namespace Norvia.IntegrationConsole.Application.Audit;

/// <summary>
/// Where the console records what operators did. The console keeps nothing of its own (ADR 0017), so the adapter writes
/// the event to the estate's telemetry, where it is kept as long as everything else.
/// </summary>
public interface IAuditLog
{
    Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken);
}
