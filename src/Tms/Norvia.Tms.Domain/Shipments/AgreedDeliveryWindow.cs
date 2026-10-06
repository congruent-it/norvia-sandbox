using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.Shipments;

/// <summary>
/// One delivery window the Planner agreed with the Carrier for a Shipment after collection (P7). The last one set counts and
/// earlier ones are kept. The Transport Order keeps the Shipper's original window.
/// </summary>
/// <param name="ExceptionNumber">The Exception the Planner set it from.</param>
/// <param name="CorrelationId">The Message trail the TMS started for it, as it starts in the TMS UI.</param>
public sealed record AgreedDeliveryWindow(TimeWindow Window, string ExceptionNumber, CorrelationId CorrelationId, DateTimeOffset AgreedAt);
