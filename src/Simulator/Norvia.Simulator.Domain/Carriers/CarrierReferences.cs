using Norvia.Simulator.Domain.Worlds;

namespace Norvia.Simulator.Domain.Carriers;

/// <summary>
/// The Carrier's own identifiers for what it answers and reports, started with the Carrier Persona's booking reference prefix
/// as the Partner portal does, and carrying the run and order so a run's messages are recognisable and never repeat an ID.
/// </summary>
public static class CarrierReferences
{
    /// <summary>The Carrier's own booking reference, which it confirms with and which Norvia matches tracking on.</summary>
    public static string BookingReference(SimulatedCarrier carrier, int runNumber, int step) => $"{PrefixOf(carrier)}-SIM{runNumber}-{step}";

    /// <summary>The Carrier's ID for one of its tracking events, unique per Carrier.</summary>
    public static string EventId(SimulatedCarrier carrier, int runNumber, int step, CarrierEvent carrierEvent) => $"{BookingReference(carrier, runNumber, step)}-{StatusCode(carrierEvent)}";

    /// <summary>The Carrier's ID for the Proof of delivery.</summary>
    public static string DocumentId(SimulatedCarrier carrier, int runNumber, int step) => $"{BookingReference(carrier, runNumber, step)}-POD";

    /// <summary>The shared status code list of the Carrier tracking format (P5).</summary>
    public static string StatusCode(CarrierEvent carrierEvent) => carrierEvent switch
    {
        CarrierEvent.Collected => "PU",
        CarrierEvent.Departed => "DEP",
        CarrierEvent.Arrived => "ARR",
        CarrierEvent.OutForDelivery => "OFD",
        CarrierEvent.Delivered => "POD",
        CarrierEvent.CollectionFailed => "NPU",
        CarrierEvent.DeliveryFailed => "NDL",
        CarrierEvent.Damaged => "DMG",
        _ => throw new ArgumentOutOfRangeException(nameof(carrierEvent), carrierEvent, "Every event has a status code."),
    };

    private static string PrefixOf(SimulatedCarrier carrier)
    {
        ArgumentNullException.ThrowIfNull(carrier);
        return carrier.BookingReferencePrefix ?? throw new InvalidOperationException($"Carrier {carrier.Id} has no Persona with a booking reference prefix to play it by.");
    }
}
