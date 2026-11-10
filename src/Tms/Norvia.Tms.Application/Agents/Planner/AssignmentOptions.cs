using Norvia.Tms.Application.Fleet.Planning;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.Agents.Planner;

/// <summary>
/// Where a Transport Order can go, for the Planner agent's one tool. Its places are <see cref="PlaceView"/>s, so no street address, and it
/// holds no Consignee and no file: the order's goods are totals only.
/// </summary>
public sealed record AssignmentOptions(
    string TransportOrderNumber,
    TransportOrderStatus Status,
    string? ShipperName,
    RatingResult? RatingResult,
    PlaceView? Collection,
    PlaceView? Delivery,
    DateTimeOffset? CollectionEarliest,
    DateTimeOffset? CollectionLatest,
    DateTimeOffset? DeliveryEarliest,
    DateTimeOffset? DeliveryLatest,
    decimal TotalWeightKg,
    int PalletPlaces,
    OwnFleetOption OwnFleet,
    IReadOnlyList<CarrierOption> Carriers);

/// <summary>Whether Own fleet can take the order on its collection day (P10), as the load check and the Driving-hour budget say.</summary>
public enum OwnFleetFit
{
    /// <summary>The day is open, not Over capacity with the order counted, and a Driver with a Shift has Driving hours left.</summary>
    Fits = 1,

    /// <summary>The day's capacity is less than its demand with the order counted, in weight or in pallet places. A warning; it never blocks.</summary>
    OverCapacity,

    /// <summary>The collection day is beyond the Planning horizon, so there is no load check for it. A warning; it never blocks.</summary>
    OutsideHorizon,

    /// <summary>The collection day is within the horizon but has no Planning day, so Own fleet has no Drivers or Vehicles for it.</summary>
    DayNotOpen,

    /// <summary>No Driver with a Shift that day has Driving hours left in the day, the week and the fortnight.</summary>
    NoDriverWithDrivingHoursLeft,

    /// <summary>The order has no collection day, so nothing can be checked.</summary>
    NoCollectionDay,
}

/// <param name="Refusal">The first Assignment rule an assignment to Own fleet breaks, or null when it meets them all.</param>
/// <param name="Warning">The Over capacity or out-of-horizon warning P3 would show on assignment, or null.</param>
/// <param name="Load">The collection day's load check with the order counted; null when outside the horizon or the day is not open.</param>
/// <param name="AvailableVehicles">Active Vehicles that are available that day.</param>
/// <param name="AvailableDrivers">Active Drivers with a Shift that day.</param>
/// <param name="DriversWithDrivingHoursLeft">Of them, how many have Driving hours left today, this week and this fortnight.</param>
/// <param name="MostDrivingHoursLeft">The most Driving hours any one of them has left on the day; null when none has a Shift.</param>
public sealed record OwnFleetOption(
    AssignmentRefusal? Refusal,
    OwnFleetFit Fit,
    DateOnly? Day,
    LoadCheckWarning? Warning,
    DayLoadView? Load,
    int AvailableVehicles,
    int AvailableDrivers,
    int DriversWithDrivingHoursLeft,
    TimeSpan? MostDrivingHoursLeft);

/// <param name="Refusal">The first Assignment rule an assignment to this Carrier breaks, or null when it meets them all.</param>
/// <param name="CarrierCost">What the Carrier Rate Card prices the order at, in EUR (Norvia's one currency), without VAT; null when it cannot be priced.</param>
/// <param name="NotPriced">Why it cannot be priced (<see cref="AssignmentRefusalCode.NoMatchingLane"/> and its kin); null when it is priced.</param>
public sealed record CarrierOption(string CarrierId, string Name, AssignmentRefusal? Refusal, decimal? CarrierCost, AssignmentRefusalCode? NotPriced);
