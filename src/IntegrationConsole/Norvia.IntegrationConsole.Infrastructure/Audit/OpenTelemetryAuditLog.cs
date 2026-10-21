using System.Globalization;
using Microsoft.Extensions.Logging;
using Norvia.IntegrationConsole.Application.Audit;
using Norvia.IntegrationConsole.Infrastructure.Trails;

namespace Norvia.IntegrationConsole.Infrastructure.Audit;

/// <summary>
/// Writes an audit event as a log record, which the OpenTelemetry pipeline of <c>ServiceDefaults</c> exports to Application
/// Insights with the rest of the estate's telemetry, so it is kept as long as that is. The tracking contract's keys go in the
/// logging scope, as an application's do (coding guidelines §7), so the event carries the correlation ID and joins its
/// Message trail. A message's body is never part of it; a discard's reason is.
/// </summary>
internal sealed partial class OpenTelemetryAuditLog(ILogger<OpenTelemetryAuditLog> logger) : IAuditLog
{
    public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);

        var keys = new Dictionary<string, object>(StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(auditEvent.CorrelationId))
        {
            keys["correlationId"] = auditEvent.CorrelationId;
        }

        foreach (var (key, kind) in TrackedBusinessKeys.All)
        {
            if (auditEvent.BusinessKeys.FirstOrDefault(businessKey => businessKey.Kind == kind) is { } found)
            {
                keys[key] = found.Value;
            }
        }

        var businessKeys = string.Join(", ", auditEvent.BusinessKeys.Select(key => $"{key.Kind} {key.Value}"));
        var at = auditEvent.At.ToString("O", CultureInfo.InvariantCulture);
        using (logger.BeginScope(keys))
        {
            Audited(logger, auditEvent.Actor, auditEvent.Action, auditEvent.Entity, auditEvent.MessageId, businessKeys, at, auditEvent.Reason ?? string.Empty);
        }

        return Task.CompletedTask;
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "Audit: {Actor} did {Action} on message {MessageId} of {Entity}, business keys [{BusinessKeys}], at {At}, reason: {Reason}")]
    private static partial void Audited(ILogger logger, string actor, string action, string entity, string messageId, string businessKeys, string at, string reason);
}
