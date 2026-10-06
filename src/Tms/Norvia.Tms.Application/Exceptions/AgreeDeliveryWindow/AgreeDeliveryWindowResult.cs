namespace Norvia.Tms.Application.Exceptions.AgreeDeliveryWindow;

/// <summary>What agreeing a delivery window did.</summary>
/// <param name="CorrelationId">The agreement's own Message trail.</param>
public sealed record AgreeDeliveryWindowResult(
    string ExceptionNumber, string ShipmentNumber, DateTimeOffset Earliest, DateTimeOffset Latest, string CorrelationId);
