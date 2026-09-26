using Norvia.IntegrationConsole.Application.Trails;

namespace Norvia.IntegrationConsole.Infrastructure.Trails;

/// <summary>The tracking contract's business keys (coding guidelines §7), under the names every flow records them by.</summary>
internal static class TrackedBusinessKeys
{
    public static readonly IReadOnlyList<(string Key, BusinessKeyKind Kind)> All =
    [
        ("transportOrderNumber", BusinessKeyKind.TransportOrderNumber),
        ("shipmentNumber", BusinessKeyKind.ShipmentNumber),
        ("carrierBookingNumber", BusinessKeyKind.CarrierBookingNumber),
        ("tripNumber", BusinessKeyKind.TripNumber),
        ("sourceMessageId", BusinessKeyKind.SourceMessageId),
    ];
}
