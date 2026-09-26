namespace Norvia.IntegrationConsole.Application.Trails;

/// <summary>The business keys of the tracking contract (coding guidelines §7).</summary>
public enum BusinessKeyKind
{
    TransportOrderNumber,
    ShipmentNumber,
    CarrierBookingNumber,
    TripNumber,

    /// <summary>A Carrier's event ID or document ID, or a Driver app event ID.</summary>
    SourceMessageId,
}
