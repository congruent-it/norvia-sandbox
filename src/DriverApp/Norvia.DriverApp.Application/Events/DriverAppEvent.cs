using System.Text.Json.Serialization;

namespace Norvia.DriverApp.Application.Events;

/// <summary>
/// An event the Driver app publishes to Service Bus for the Driver reports Logic App (ADR 0003: events out, ADR 0016). Its body follows
/// the Canonical Model; <see cref="Topic"/>, <see cref="SessionId"/> and <see cref="ApplicationProperties"/> travel on the message, not
/// in the body. Every event carries the Driver app event ID, the Trip number, the version the Driver app held, the Driver ID from the
/// token, the event time with its offset and the correlation ID.
/// </summary>
public abstract record DriverAppEvent(Guid EventId, DateTimeOffset OccurredAt, string CorrelationId, string TripNumber, int Version, string DriverId)
{
    public abstract string EventType { get; }

    public abstract int EventVersion { get; }

    /// <summary>The Service Bus topic the event is published to.</summary>
    [JsonIgnore]
    public string Topic => TopicName;

    /// <summary>The Trip number: the topic's subscription uses sessions, so one Trip's reports are read one at a time and in the order they were published.</summary>
    [JsonIgnore]
    public string SessionId => TripNumber;

    // Not public, so it stays out of the serialized body; an override wouldn't inherit a [JsonIgnore].
    protected virtual string TopicName => Topics.DriverAppEvents;

    /// <summary>The tracking-contract keys the event knows (coding guidelines §7), plus its type and version, so Service Bus's own dead-lettering leaves a message that can still be found.</summary>
    [JsonIgnore]
    public IReadOnlyDictionary<string, object> ApplicationProperties => new Dictionary<string, object>
    {
        ["eventType"] = EventType,
        ["eventVersion"] = EventVersion,
        [TrackingKeys.CorrelationId] = CorrelationId,
        [TrackingKeys.Flow] = Flows.TripExecution,
        [TrackingKeys.PartyId] = DriverId,
        [TrackingKeys.TripNumber] = TripNumber,
        [TrackingKeys.SourceMessageId] = EventId.ToString(),
    };
}
