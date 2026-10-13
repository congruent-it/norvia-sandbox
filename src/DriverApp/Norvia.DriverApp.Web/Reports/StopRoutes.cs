using Norvia.DriverApp.Application.Reports;
using Norvia.DriverApp.Application.Trips;

namespace Norvia.DriverApp.Web.Reports;

/// <summary>The addresses of a Stop's screen and of the form posts of what the Driver does there.</summary>
internal static class StopRoutes
{
    public static string Screen(string tripNumber, string shipmentNumber, StopKindDto kind) =>
        $"/trips/{Uri.EscapeDataString(tripNumber)}/stops/{Uri.EscapeDataString(shipmentNumber)}/{Segment(kind)}";

    public static string Post(string tripNumber, string shipmentNumber, StopKindDto kind, StopActionDto action) =>
        $"{Screen(tripNumber, shipmentNumber, kind)}/{Segment(action)}";

    public static string Segment(StopKindDto kind) => kind == StopKindDto.Collection ? "collection" : "delivery";

    public static string Segment(StopActionDto action) => action switch
    {
        StopActionDto.Collected => "collected",
        StopActionDto.CollectionFailed => "collection-failed",
        StopActionDto.DeliveryFailed => "delivery-failed",
        _ => "damaged",
    };

    public static bool TryParse(string segment, out StopKindDto kind)
    {
        kind = segment switch { "collection" => StopKindDto.Collection, _ => StopKindDto.Delivery };
        return segment is "collection" or "delivery";
    }

    public static bool TryParse(string segment, out StopActionDto action)
    {
        action = segment switch
        {
            "collected" => StopActionDto.Collected,
            "collection-failed" => StopActionDto.CollectionFailed,
            "delivery-failed" => StopActionDto.DeliveryFailed,
            _ => StopActionDto.Damaged,
        };
        return segment is "collected" or "collection-failed" or "delivery-failed" or "damaged";
    }
}
