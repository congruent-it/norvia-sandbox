namespace Norvia.Tms.Domain.Trips;

/// <summary>
/// Why the TMS refused a Planner's action on a Trip, apart from the availability rules of P10, whose codes are
/// <see cref="Fleet.TripAvailabilityCode"/>. The names are stable codes: never rename one, only add.
/// </summary>
public enum TripRefusalCode
{
    /// <summary>There is no Trip with that number.</summary>
    TripNotFound = 1,

    /// <summary>The Trip is Dispatched, so it can't be changed by hand as a Draft can, nor discarded.</summary>
    TripNotDraft,

    /// <summary>The planned start is not inside the Driver's Shift that day.</summary>
    OutsideShift,

    /// <summary>There is no Shipment with that number.</summary>
    ShipmentNotFound,

    /// <summary>The Shipment is not a Released Own fleet Shipment, so it can't go on a Trip.</summary>
    ShipmentNotReleased,

    /// <summary>The Shipment is on another Trip already.</summary>
    ShipmentOnAnotherTrip,

    /// <summary>The Shipment collects on another day than the Trip's.</summary>
    CollectionDayMismatch,

    /// <summary>A Shipment's delivery Stop comes before its collection Stop.</summary>
    CollectionBeforeDelivery,

    /// <summary>The order given doesn't name every Stop of the Trip once, and no others.</summary>
    StopsNotTheTripsStops,

    /// <summary>The Trip lacks a Vehicle, a Driver, a planned start, a planned driving duration or a Shipment, so it can't be dispatched.</summary>
    TripIncomplete,
}
