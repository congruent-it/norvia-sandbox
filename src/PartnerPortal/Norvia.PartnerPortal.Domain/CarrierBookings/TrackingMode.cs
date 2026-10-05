namespace Norvia.PartnerPortal.Domain.CarrierBookings;

/// <summary>How a Carrier Persona sends the Tracking events of the Shipments it confirmed.</summary>
public enum TrackingMode
{
    /// <summary>The presenter sends each Tracking event by hand.</summary>
    Manual,

    /// <summary>After confirmation the Persona sends Collected, Departed, Out for delivery and Delivered in turn, with a delay between each.</summary>
    AutoProgress,
}
