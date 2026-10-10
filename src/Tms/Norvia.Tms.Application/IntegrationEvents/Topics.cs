namespace Norvia.Tms.Application.IntegrationEvents;

public static class Topics
{
    public const string TransportOrderEvents = "transport-order-events";

    /// <summary>Where the TMS publishes what happens to Shipments, from their planning (P3) on.</summary>
    public const string ShipmentEvents = "shipment-events";

    /// <summary>Where the TMS publishes what happens to Trips, from their dispatch (P11) on.</summary>
    public const string TripEvents = "trip-events";
}
