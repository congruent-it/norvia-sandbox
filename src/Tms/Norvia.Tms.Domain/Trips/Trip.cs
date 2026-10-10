using Ardalis.SharedKernel;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.Trips;

/// <summary>
/// One run on one day by one Vehicle and one Driver (P11). A Draft holds its Vehicle and Driver, so neither has another
/// Trip that day; the rules about who may hold them are P10's, and the handlers apply them before a Draft changes.
/// </summary>
public sealed class Trip : EntityBase<TripId>, IAggregateRoot
{
    private Trip(TripNumber number, DateOnly day, VehicleId? vehicleId, DriverId? driverId, IdempotencyKey idempotencyKey)
    {
        Id = TripId.New();
        Number = number;
        Day = day;
        VehicleId = vehicleId;
        DriverId = driverId;
        Status = TripStatus.Draft;
        IdempotencyKey = idempotencyKey;
    }

    private readonly List<TripStop> _stops = [];

    public TripNumber Number { get; private set; }

    public DateOnly Day { get; private set; }

    public TripStatus Status { get; private set; }

    /// <summary>0 while the Trip is a Draft, 1 from Dispatch, and up with each Amend (ADR 0015).</summary>
    public int Version { get; private set; }

    public VehicleId? VehicleId { get; private set; }

    public DriverId? DriverId { get; private set; }

    /// <summary>When the Trip starts, a time on <see cref="Day"/> that is inside the Driver's Shift. Empty until the Planner enters it.</summary>
    public TimeOnly? PlannedStart { get; private set; }

    /// <summary>How long the Driver drives on the Trip, which uses up their Driving-hour budget. Empty until the Planner enters it.</summary>
    public TimeSpan? PlannedDrivingDuration { get; private set; }

    /// <summary>The driving this Trip uses up, or null while the Trip has no Driver or no duration.</summary>
    public PlannedDriving? PlannedDriving =>
        DriverId is { } driver && PlannedDrivingDuration is { } duration ? new PlannedDriving(driver, Day, duration) : null;

    /// <summary>The most driving a Trip can plan: a day.</summary>
    public static readonly TimeSpan MaxPlannedDrivingDuration = TimeSpan.FromHours(24);

    /// <summary>True when the duration is more than nothing and at most <see cref="MaxPlannedDrivingDuration"/>.</summary>
    public static bool IsPlannableDuration(TimeSpan duration) => duration > TimeSpan.Zero && duration <= MaxPlannedDrivingDuration;

    /// <summary>The Stops in the order the Driver visits them: each Shipment's collection, then its delivery, in the order the Shipments were added.</summary>
    public IReadOnlyList<TripStop> Stops => [.. _stops.OrderBy(stop => stop.Position)];

    /// <summary>The Shipments on the Trip, in the order they were added.</summary>
    public IEnumerable<ShipmentId> ShipmentIds => Stops.Select(stop => stop.ShipmentId).Distinct();

    public bool HasShipment(ShipmentId shipment) => Stops.Any(stop => stop.ShipmentId == shipment);

    /// <summary>The key of the request that created the Trip, so the same request sent again as a retry finds it instead of creating another.</summary>
    public IdempotencyKey IdempotencyKey { get; private set; }

    /// <summary>A new Draft. Either of the Vehicle and the Driver may be empty.</summary>
    public static Trip CreateDraft(TripNumber number, DateOnly day, VehicleId? vehicleId, DriverId? driverId, IdempotencyKey idempotencyKey)
    {
        ArgumentNullException.ThrowIfNull(number);
        ArgumentNullException.ThrowIfNull(idempotencyKey);
        return new Trip(number, day, vehicleId, driverId, idempotencyKey);
    }

    /// <summary>
    /// Gives the Draft this Vehicle and Driver, or none, which gives the previous ones back. The caller checks <see cref="EnsureDraft"/>
    /// first, so a Dispatched Trip reaching here is a bug.
    /// </summary>
    public void Pick(VehicleId? vehicleId, DriverId? driverId)
    {
        if (EnsureDraft() is not null)
        {
            throw new InvalidOperationException($"Trip {Number} is Dispatched, so its Vehicle and Driver change only by Amend.");
        }

        VehicleId = vehicleId;
        DriverId = driverId;
    }

    /// <summary>
    /// Sets the planned start and driving duration of the Draft, or gives them back with null. The caller checks the Shift and the
    /// Driving-hour budget first, as for <see cref="Pick"/>.
    /// </summary>
    public void Plan(TimeOnly? start, TimeSpan? drivingDuration)
    {
        if (EnsureDraft() is not null)
        {
            throw new InvalidOperationException($"Trip {Number} is Dispatched, so its planned start and duration change only by Amend.");
        }

        if (drivingDuration is { } duration && !IsPlannableDuration(duration))
        {
            throw new ArgumentOutOfRangeException(nameof(drivingDuration), duration, "A driving duration is more than nothing and at most a day.");
        }

        PlannedStart = start;
        PlannedDrivingDuration = drivingDuration;
    }

    /// <summary>
    /// Puts the Shipment on the Draft: its collection Stop and then its delivery Stop go at the end of the sequence. A Shipment already
    /// on the Trip stays where it is. The caller checks the rules first, as for <see cref="Pick"/>.
    /// </summary>
    public void AddShipment(ShipmentId shipment)
    {
        if (EnsureDraft() is not null)
        {
            throw new InvalidOperationException($"Trip {Number} is Dispatched, so its Shipments change only by Amend.");
        }

        if (HasShipment(shipment))
        {
            return;
        }

        _stops.Add(new TripStop(shipment, StopKind.Collection, _stops.Count));
        _stops.Add(new TripStop(shipment, StopKind.Delivery, _stops.Count));
    }

    /// <summary>
    /// Puts the Draft's Stops in this order. The order must name each Stop of the Trip once and no others, and every Shipment's collection must
    /// come before its delivery; otherwise the Trip is left as it was and the fault says why. Two Stops at the same address stay two Stops,
    /// since a Stop is a Shipment's collection or delivery, not a place. The caller checks <see cref="EnsureDraft"/> first, as for <see cref="Pick"/>.
    /// </summary>
    public StopOrderFault? ReorderStops(IReadOnlyList<(ShipmentId Shipment, StopKind Kind)> order)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (EnsureDraft() is not null)
        {
            throw new InvalidOperationException($"Trip {Number} is Dispatched, so its Stops change only by Amend.");
        }

        if (order.Count != _stops.Count || !_stops.Select(stop => (stop.ShipmentId, stop.Kind)).ToHashSet().SetEquals(order))
        {
            return new StopOrderFault(TripRefusalCode.StopsNotTheTripsStops);
        }

        var delivered = new HashSet<ShipmentId>();
        foreach (var (shipment, kind) in order)
        {
            if (kind == StopKind.Delivery)
            {
                delivered.Add(shipment);
            }
            else if (delivered.Contains(shipment))
            {
                return new StopOrderFault(TripRefusalCode.CollectionBeforeDelivery, shipment);
            }
        }

        var reordered = order.Select((stop, position) => new TripStop(stop.Shipment, stop.Kind, position)).ToList();
        if (!reordered.SequenceEqual(Stops))
        {
            _stops.Clear();
            _stops.AddRange(reordered);
        }

        return null;
    }

    /// <summary>What Dispatch still needs, each as "a Vehicle", "a Driver" and so on; empty when the Trip is complete.</summary>
    public IReadOnlyList<string> MissingParts()
    {
        var missing = new List<string>();
        if (VehicleId is null)
        {
            missing.Add("a Vehicle");
        }

        if (DriverId is null)
        {
            missing.Add("a Driver");
        }

        if (PlannedStart is null)
        {
            missing.Add("a planned start");
        }

        if (PlannedDrivingDuration is null)
        {
            missing.Add("a planned driving duration");
        }

        if (_stops.Count == 0)
        {
            missing.Add("at least one Shipment");
        }

        return missing;
    }

    /// <summary>
    /// Sends the complete Draft to its Driver as version 1: every one of its Shipments becomes Dispatched, and <see cref="TripDispatchedEvent"/> is
    /// raised. The caller checks <see cref="EnsureDraft"/>, <see cref="MissingParts"/> and the Trip rules first, as for <see cref="Pick"/>.
    /// </summary>
    public void Dispatch(CorrelationId correlationId, DateTimeOffset dispatchedAt, Vehicle vehicle, Driver driver, IReadOnlyList<DispatchedShipment> shipments)
    {
        ArgumentNullException.ThrowIfNull(correlationId);
        ArgumentNullException.ThrowIfNull(vehicle);
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentNullException.ThrowIfNull(shipments);
        if (EnsureDraft() is not null)
        {
            throw new InvalidOperationException($"Trip {Number} is Dispatched already.");
        }

        if (MissingParts() is { Count: > 0 } missing)
        {
            throw new InvalidOperationException($"Trip {Number} can't be dispatched without {string.Join(", ", missing)}.");
        }

        foreach (var shipment in shipments.Select(item => item.Shipment))
        {
            if (!HasShipment(shipment.Id) || !shipment.Dispatch())
            {
                throw new InvalidOperationException($"Shipment {shipment.Number} can't be dispatched on Trip {Number}.");
            }
        }

        Status = TripStatus.Dispatched;
        Version = 1;
        RegisterDomainEvent(new TripDispatchedEvent(this, correlationId, dispatchedAt, vehicle, driver, shipments));
    }

    /// <summary>Null when the Trip is a Draft, which can be changed or discarded; a Dispatched Trip is neither, and is never deleted.</summary>
    public TripRefusal? EnsureDraft() => Status == TripStatus.Draft ? null : TripRefusal.TripNotDraft(Number);
}
