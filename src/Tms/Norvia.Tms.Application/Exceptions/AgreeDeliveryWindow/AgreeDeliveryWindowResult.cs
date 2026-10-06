namespace Norvia.Tms.Application.Exceptions.AgreeDeliveryWindow;

/// <summary>What agreeing a delivery window did.</summary>
/// <param name="CorrelationId">The agreement's own Message trail; a repeat answers with the trail of the agreement it repeats.</param>
/// <param name="Repeat">True when the agreement was sent again under its key, and answered as before without agreeing again.</param>
public sealed record AgreeDeliveryWindowResult(
    string ExceptionNumber, string ShipmentNumber, DateTimeOffset Earliest, DateTimeOffset Latest, string CorrelationId, bool Repeat = false);
