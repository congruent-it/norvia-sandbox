using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.Exceptions;

namespace Norvia.Tms.Application.Exceptions.Resolve;

/// <summary>
/// The Canonical Model event <c>ExceptionResolved</c>, version 1 (contracts/canonical-model/exception-resolved.v1.schema.json).
/// The Planner resolved an Exception (P7), under the resolution's own correlation ID.
/// </summary>
/// <param name="OccurredAt">When the Planner resolved the Exception.</param>
/// <param name="ExceptionType">What went wrong: <c>DeliveryFailed</c>.</param>
/// <param name="Outcome">One of <c>NoActionNeeded</c>, <c>Redelivered</c>, <c>Replanned</c>, <c>ReturnedToShipper</c> and <c>ClaimHandledOutsideTms</c>.</param>
/// <param name="Remark">The Planner's remark.</param>
public sealed record ExceptionResolvedV1(
    Guid EventId,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    string ExceptionNumber,
    string ShipmentNumber,
    string TransportOrderNumber,
    string ExceptionType,
    string Outcome,
    string Remark)
    : IntegrationEvent(EventId, OccurredAt, CorrelationId)
{
    public override string EventType => "ExceptionResolved";

    public override int EventVersion => 1;

    protected override string TopicName => Topics.ShipmentEvents;

    public static ExceptionResolvedV1 From(ExceptionResolvedEvent resolved)
    {
        ArgumentNullException.ThrowIfNull(resolved);
        var exception = resolved.Exception;
        return new(
            Guid.CreateVersion7(),
            exception.ResolvedAt!.Value,
            exception.ResolutionCorrelationId!.Value,
            exception.Number.Value,
            exception.ShipmentNumber.Value,
            exception.TransportOrderNumber.Value,
            exception.Type.ToString(),
            exception.Outcome!.Value.ToString(),
            exception.Remark!);
    }

    protected override IEnumerable<KeyValuePair<string, object>> TrackingProperties() =>
    [
        new(TrackingKeys.Flow, Flows.ExceptionHandling),
        new(TrackingKeys.TransportOrderNumber, TransportOrderNumber),
        new(TrackingKeys.ShipmentNumber, ShipmentNumber),
    ];
}
