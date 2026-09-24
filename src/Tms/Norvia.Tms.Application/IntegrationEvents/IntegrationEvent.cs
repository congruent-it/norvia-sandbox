using System.Text.Json.Serialization;

namespace Norvia.Tms.Application.IntegrationEvents;

/// <summary>
/// An event the TMS publishes to Service Bus for other applications (ADR 0003: events out). Its body follows the
/// Canonical Model; <see cref="Topic"/> and <see cref="ApplicationProperties"/> travel on the message, not in the body.
/// </summary>
public abstract record IntegrationEvent(Guid EventId, DateTimeOffset OccurredAt, string CorrelationId)
{
    public abstract string EventType { get; }

    public abstract int EventVersion { get; }

    /// <summary>The Service Bus topic the event is published to.</summary>
    [JsonIgnore]
    public string Topic => TopicName;

    /// <summary>The tracking-contract keys the event knows (coding guidelines §7), plus its type and version.</summary>
    [JsonIgnore]
    public IReadOnlyDictionary<string, object> ApplicationProperties =>
        new Dictionary<string, object>(TrackingProperties())
        {
            ["eventType"] = EventType,
            ["eventVersion"] = EventVersion,
            [TrackingKeys.CorrelationId] = CorrelationId,
        };

    // Not public, so it stays out of the serialized body; an override wouldn't inherit a [JsonIgnore].
    protected abstract string TopicName { get; }

    protected abstract IEnumerable<KeyValuePair<string, object>> TrackingProperties();
}
