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
    private readonly List<TripVersionDelivery> _deliveries = [];

    public TripNumber Number { get; private set; }

    public DateOnly Day { get; private set; }

    public TripStatus Status { get; private set; }

    /// <summary>0 while the Trip is a Draft, 1 from Dispatch, and up with each Amend (ADR 0015).</summary>
    public int Version { get; private set; }

    /// <summary>Whether the Driver app has each version, in version order. A Draft has none.</summary>
    public IReadOnlyList<TripVersionDelivery> Deliveries => _deliveries;

    /// <summary>Whether the Driver app has the Trip's latest version; null while the Trip is a Draft.</summary>
    public TripVersionDelivery? LatestDelivery => DeliveryOf(Version);

    /// <summary>The delivery of this version, or null when the Trip never had it.</summary>
    public TripVersionDelivery? DeliveryOf(int version) => _deliveries.Find(delivery => delivery.Version == version);

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
    /// Gives the Trip this Vehicle and Driver, or none, which gives the previous ones back; true when that changed anything. The caller
    /// checks <see cref="EnsureAmendable"/> first, so a Trip that is under way reaching here is a bug. On a Dispatched Trip the caller
    /// follows a change with <see cref="Amend"/>.
    /// </summary>
    public bool Pick(VehicleId? vehicleId, DriverId? driverId)
    {
        ThrowIfUnderWay();
        var changed = VehicleId != vehicleId || DriverId != driverId;
        VehicleId = vehicleId;
        DriverId = driverId;
        return changed;
    }

    /// <summary>
    /// Sets the planned start and driving duration of the Trip, or gives them back with null; true when that changed anything. The
    /// caller checks the Shift and the Driving-hour budget first, as for <see cref="Pick"/>.
    /// </summary>
    public bool Plan(TimeOnly? start, TimeSpan? drivingDuration)
    {
        ThrowIfUnderWay();

        if (drivingDuration is { } duration && !IsPlannableDuration(duration))
        {
            throw new ArgumentOutOfRangeException(nameof(drivingDuration), duration, "A driving duration is more than nothing and at most a day.");
        }

        var changed = PlannedStart != start || PlannedDrivingDuration != drivingDuration;
        PlannedStart = start;
        PlannedDrivingDuration = drivingDuration;
        return changed;
    }

    /// <summary>
    /// Puts the Shipment on the Trip: its collection Stop and then its delivery Stop go at the end of the sequence. A Shipment already
    /// on the Trip stays where it is and the answer is false. A <paramref name="redelivery"/> (P12) has only its delivery Stop, since its
    /// collection is done. The caller checks the rules first, as for <see cref="Pick"/>.
    /// </summary>
    public bool AddShipment(ShipmentId shipment, bool redelivery = false)
    {
        ThrowIfUnderWay();
        if (HasShipment(shipment))
        {
            return false;
        }

        if (!redelivery)
        {
            _stops.Add(new TripStop(shipment, StopKind.Collection, _stops.Count));
        }

        _stops.Add(new TripStop(shipment, StopKind.Delivery, _stops.Count));
        return true;
    }

    /// <summary>Puts the Shipment on the Trip as <see cref="AddShipment(ShipmentId, bool)"/> does, as a Re-delivery when it is Awaiting re-delivery (P12).</summary>
    public bool AddShipment(Shipment shipment)
    {
        ArgumentNullException.ThrowIfNull(shipment);
        return AddShipment(shipment.Id, shipment.AwaitingRedelivery);
    }

    /// <summary>True when the Shipment is on the Trip with only its delivery Stop: a Re-delivery (P12).</summary>
    public bool IsRedelivery(ShipmentId shipment) =>
        _stops.Any(stop => stop.ShipmentId == shipment) && !_stops.Any(stop => stop.ShipmentId == shipment && stop.Kind == StopKind.Collection);

    /// <summary>
    /// Puts the Trip's Stops in this order. The order must name each Stop of the Trip once and no others, and every Shipment's collection must
    /// come before its delivery; otherwise the Trip is left as it was and the fault says why. Two Stops at the same address stay two Stops,
    /// since a Stop is a Shipment's collection or delivery, not a place. The caller checks <see cref="EnsureAmendable"/> first, as for <see cref="Pick"/>.
    /// </summary>
    public StopOrderFault? ReorderStops(IReadOnlyList<(ShipmentId Shipment, StopKind Kind)> order)
    {
        ArgumentNullException.ThrowIfNull(order);
        ThrowIfUnderWay();

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
        _deliveries.Add(TripVersionDelivery.Start(Version));
        RegisterDomainEvent(new TripDispatchedEvent(this, correlationId, dispatchedAt, vehicle, driver, shipments));
    }

    /// <summary>Null when the Trip is a Draft, which can be changed or discarded; a Dispatched Trip is neither, and is never deleted.</summary>
    public TripRefusal? EnsureDraft() => Status == TripStatus.Draft ? null : TripRefusal.TripNotDraft(Number);

    /// <summary>
    /// Null when the Planner can change the Trip: a Draft, or a Dispatched Trip, which an Amend changes under the same rules. Once the Trip is In
    /// progress (P12) nothing but taking off an uncollected Shipment is allowed, which is <see cref="TripRefusalCode.TripUnderWay"/> for the rest.
    /// </summary>
    public TripRefusal? EnsureAmendable() => Status switch
    {
        TripStatus.InProgress => TripRefusal.TripUnderWay(Number),
        TripStatus.Completed => TripRefusal.TripCompleted(Number),
        _ => null,
    };

    /// <summary>
    /// Null when the Driver can start the Trip (P12): it is theirs, it is Dispatched and it is its day. A Trip that is another Driver's is
    /// <see cref="TripRefusalCode.TripNotFound"/>, as the Driver app shows a Driver only their own Trips. <paramref name="today"/> is the day
    /// it is now where Norvia works.
    /// </summary>
    public TripRefusal? EnsureCanStart(DriverId driver, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(driver);
        if (DriverId != driver)
        {
            return TripRefusal.TripNotFound(Number.Value);
        }

        if (Status != TripStatus.Dispatched)
        {
            return TripRefusal.TripNotDispatched(Number, Status);
        }

        return today == Day ? null : TripRefusal.NotTripDay(Number, Day, today);
    }

    /// <summary>
    /// The Driver started the Trip (P12): it is In progress, at the same version, and <see cref="TripStartedEvent"/> is raised. The caller
    /// checks <see cref="EnsureCanStart"/> first.
    /// </summary>
    public void Start(CorrelationId correlationId, DateTimeOffset startedAt)
    {
        ArgumentNullException.ThrowIfNull(correlationId);
        if (Status != TripStatus.Dispatched || DriverId is not { } driver)
        {
            throw new InvalidOperationException($"Trip {Number} is {Status}, and only a Dispatched Trip can start.");
        }

        Status = TripStatus.InProgress;
        RegisterDomainEvent(new TripStartedEvent(this, driver, correlationId, startedAt));
    }

    /// <summary>
    /// Null when the Driver can end the Trip (P12): it is theirs, it is In progress and every Stop has an outcome. A Trip that is another
    /// Driver's is <see cref="TripRefusalCode.TripNotFound"/>, as for <see cref="EnsureCanStart"/>.
    /// </summary>
    public TripRefusal? EnsureCanEnd(DriverId driver, StopOutcomes outcomes)
    {
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentNullException.ThrowIfNull(outcomes);
        if (DriverId != driver)
        {
            return TripRefusal.TripNotFound(Number.Value);
        }

        if (Status != TripStatus.InProgress)
        {
            return TripRefusal.TripNotUnderWay(Number, Status);
        }

        return outcomes.OpenStopsOf(this) is { Count: > 0 } open ? TripRefusal.StopsOpen(Number, open.Count) : null;
    }

    /// <summary>
    /// The Driver ended the Trip (P12): it is Completed, at the same version, and <see cref="TripCompletedEvent"/> is raised with the
    /// Shipments delivered, not delivered and not collected, which are returned too. An uncollected Shipment goes back to Planned and a collected one that wasn't
    /// delivered is flagged Awaiting re-delivery, keeping its status. Both leave the Trip, which keeps only the Stops of the delivered ones, and
    /// no <c>ShipmentTakenOffTrip</c> is raised. <paramref name="shipments"/> are the tracked Shipments on the Trip. The caller checks <see cref="EnsureCanEnd"/> first.
    /// </summary>
    public TripShipmentGroups End(CorrelationId correlationId, DateTimeOffset endedAt, StopOutcomes outcomes, IReadOnlyList<Shipment> shipments)
    {
        ArgumentNullException.ThrowIfNull(correlationId);
        ArgumentNullException.ThrowIfNull(outcomes);
        ArgumentNullException.ThrowIfNull(shipments);
        if (Status != TripStatus.InProgress || DriverId is not { } driver || outcomes.OpenStopsOf(this).Count > 0)
        {
            throw new InvalidOperationException($"Trip {Number} is {Status}, and only an In progress Trip with an outcome for every Stop ends.");
        }

        List<ShipmentNumber> delivered = [], notDelivered = [], notCollected = [];
        foreach (var id in ShipmentIds)
        {
            var shipment = shipments.FirstOrDefault(candidate => candidate.Id == id)
                ?? throw new ArgumentException($"Trip {Number} carries a Shipment that isn't among the ones given.", nameof(shipments));
            switch (outcomes.ResultOf(id))
            {
                case ShipmentTripResult.Delivered:
                    delivered.Add(shipment.Number);
                    break;
                case ShipmentTripResult.NotCollected when shipment.TakeOffTrip():
                    notCollected.Add(shipment.Number);
                    break;
                case ShipmentTripResult.NotDelivered when shipment.AwaitRedelivery():
                    notDelivered.Add(shipment.Number);
                    break;
                default:
                    throw new InvalidOperationException($"Shipment {shipment.Number} can't leave Trip {Number} as {outcomes.ResultOf(id)}.");
            }
        }

        var kept = Stops
            .Where(stop => outcomes.ResultOf(stop.ShipmentId) == ShipmentTripResult.Delivered)
            .Select((stop, position) => stop with { Position = position })
            .ToList();
        _stops.Clear();
        _stops.AddRange(kept);

        Status = TripStatus.Completed;
        var groups = new TripShipmentGroups(delivered, notDelivered, notCollected);
        RegisterDomainEvent(new TripCompletedEvent(this, driver, correlationId, endedAt, groups));
        return groups;
    }

    /// <summary>
    /// Sends the Dispatched Trip, changed by the Planner, as the next version: every Shipment on it becomes Dispatched (one added by this Amend
    /// was Released), and <see cref="TripDispatchedEvent"/> is raised with the whole Trip under the Amend's own correlation ID. The caller makes
    /// the change, checks the Trip rules and <see cref="EnsureAmendable"/> first, and calls this once for it.
    /// </summary>
    public void Amend(CorrelationId correlationId, DateTimeOffset amendedAt, Vehicle vehicle, Driver driver, IReadOnlyList<DispatchedShipment> shipments)
    {
        ArgumentNullException.ThrowIfNull(correlationId);
        ArgumentNullException.ThrowIfNull(vehicle);
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentNullException.ThrowIfNull(shipments);
        if (Status != TripStatus.Dispatched)
        {
            throw new InvalidOperationException($"Trip {Number} is {Status}, and only a Dispatched Trip is amended.");
        }

        if (MissingParts() is { Count: > 0 } missing)
        {
            throw new InvalidOperationException($"Trip {Number} can't be amended to lack {string.Join(", ", missing)}.");
        }

        foreach (var shipment in shipments.Select(item => item.Shipment))
        {
            if (!HasShipment(shipment.Id) || (!WasSentInAnEarlierVersion(shipment) && !shipment.Dispatch()))
            {
                throw new InvalidOperationException($"Shipment {shipment.Number} can't be dispatched on Trip {Number}.");
            }
        }

        Version++;
        _deliveries.Add(TripVersionDelivery.Start(Version));
        RegisterDomainEvent(new TripDispatchedEvent(this, correlationId, amendedAt, vehicle, driver, shipments));
    }

    // Dispatched already by an earlier version: Dispatched, or a Re-delivery whose flag an earlier version cleared.
    private bool WasSentInAnEarlierVersion(Shipment shipment) =>
        shipment.Status == ShipmentStatus.Dispatched || (IsRedelivery(shipment.Id) && !shipment.AwaitingRedelivery);

    // A Re-delivery can't go back to Planned, because its goods are collected: it is Awaiting re-delivery again (a no-op while the flag is still set).
    private bool ReleaseFromTrip(Shipment shipment) =>
        IsRedelivery(shipment.Id) ? shipment.AwaitingRedelivery || shipment.AwaitRedelivery() : shipment.TakeOffTrip();

    private bool CanTakeOffRedelivery(Shipment shipment) =>
        IsRedelivery(shipment.Id) && shipment.CarriedBy.IsOwnFleet && shipment.Status is ShipmentStatus.Collected or ShipmentStatus.InTransit;

    /// <summary>Null when the Shipment can be taken off this Trip: it is on it and not collected yet, or a Re-delivery (P12) not delivered yet, however far the Trip has got.</summary>
    public TripRefusal? EnsureCanTakeOff(Shipment shipment)
    {
        ArgumentNullException.ThrowIfNull(shipment);
        if (!HasShipment(shipment.Id))
        {
            return TripRefusal.ShipmentNotOnTrip(shipment.Number.Value, Number);
        }

        return shipment.CanTakeOffTrip || CanTakeOffRedelivery(shipment) ? null : TripRefusal.ShipmentCollected(shipment.Number.Value, Number);
    }

    /// <summary>
    /// Takes the Shipment off the Trip: its Stops leave the sequence, which closes up, the Shipment goes back to Planned (a Re-delivery (P12) stays Collected and is Awaiting re-delivery again), and
    /// <see cref="ShipmentTakenOffTripEvent"/> is raised. On a Dispatched or In progress Trip this is an Amend too: the version goes up and
    /// <see cref="TripDispatchedEvent"/> is raised with the Shipments that remain, possibly none, under the same correlation ID; a Draft has no
    /// version to send, so <paramref name="next"/> must be null for it and given otherwise. The caller checks <see cref="EnsureCanTakeOff"/> first.
    /// </summary>
    public void TakeShipmentOff(Shipment shipment, string reason, CorrelationId correlationId, DateTimeOffset at, NextTripVersion? next = null)
    {
        ArgumentNullException.ThrowIfNull(shipment);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentNullException.ThrowIfNull(correlationId);
        if (EnsureCanTakeOff(shipment) is { } refusal)
        {
            throw new InvalidOperationException(refusal.Message);
        }

        var sent = Status != TripStatus.Draft;
        if (sent != (next is not null))
        {
            throw new ArgumentException($"Trip {Number} is {Status}, so " + (sent ? "taking a Shipment off sends the next version, which needs its contents." : "there is no next version to send."), nameof(next));
        }

        if (!ReleaseFromTrip(shipment))
        {
            throw new InvalidOperationException($"Shipment {shipment.Number} can't leave Trip {Number}.");
        }

        var kept = Stops.Where(stop => stop.ShipmentId != shipment.Id).Select((stop, position) => stop with { Position = position }).ToList();
        _stops.Clear();
        _stops.AddRange(kept);

        if (sent)
        {
            Version++;
            _deliveries.Add(TripVersionDelivery.Start(Version));
            RegisterDomainEvent(new TripDispatchedEvent(this, correlationId, at, next!.Vehicle, next.Driver, next.Shipments));
        }

        RegisterDomainEvent(new ShipmentTakenOffTripEvent(this, shipment, sent ? Version : null, reason, correlationId, at));
    }

    private void ThrowIfUnderWay()
    {
        if (EnsureAmendable() is { } refusal)
        {
            throw new InvalidOperationException(refusal.Message);
        }
    }

    /// <summary>
    /// The Trip dispatch Logic App reports the version as accepted by the Driver app. A report for a version that is no longer Sending is
    /// ignored, and one for an older version than the Trip's latest is stored against that version and changes nothing else.
    /// </summary>
    public TripDeliveryReport ReportDelivered(int version, DateTimeOffset at)
    {
        if (SendingVersion(version, out var report) is not { } delivery)
        {
            return report;
        }

        delivery.Deliver(at);
        return TripDeliveryReport.Recorded;
    }

    /// <summary>
    /// The Trip dispatch Logic App reports the version as not delivered, with its technical cause. Only the latest version raises
    /// <see cref="TripNotDeliveredEvent"/>: the Driver app holds a newer one than an older version, so nothing else changes.
    /// </summary>
    public TripDeliveryReport ReportNotDelivered(int version, string cause, CorrelationId correlationId, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cause);
        ArgumentNullException.ThrowIfNull(correlationId);
        if (SendingVersion(version, out var report) is not { } delivery)
        {
            return report;
        }

        delivery.Fail(cause, at);
        if (version == Version)
        {
            RegisterDomainEvent(new TripNotDeliveredEvent(this, version, DriverId ?? throw new InvalidOperationException($"Trip {Number} was dispatched without a Driver."), cause, correlationId, at));
        }

        return TripDeliveryReport.Recorded;
    }

    /// <summary>Null when the Planner can send this version again: the Trip's latest version, and Not delivered.</summary>
    public TripRefusal? EnsureCanSendAgain(int version)
    {
        if (Status != TripStatus.Dispatched)
        {
            return TripRefusal.TripNotDispatched(Number, Status);
        }

        if (DeliveryOf(version) is not { } delivery)
        {
            return TripRefusal.TripVersionNotFound(Number, version);
        }

        if (version != Version)
        {
            return TripRefusal.TripVersionNotLatest(Number, version, Version);
        }

        return delivery.State == TripDeliveryState.NotDelivered ? null : TripRefusal.NothingToSendAgain(Number, version, delivery.State);
    }

    /// <summary>
    /// Sends the Not delivered latest version again: it is Sending once more, and <see cref="TripDispatchedEvent"/> is raised with the same
    /// version under a new correlation ID. The caller checks <see cref="EnsureCanSendAgain"/> first.
    /// </summary>
    public void SendAgain(int version, CorrelationId correlationId, DateTimeOffset sentAt, Vehicle vehicle, Driver driver, IReadOnlyList<DispatchedShipment> shipments)
    {
        ArgumentNullException.ThrowIfNull(correlationId);
        ArgumentNullException.ThrowIfNull(vehicle);
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentNullException.ThrowIfNull(shipments);
        if (EnsureCanSendAgain(version) is { } refusal)
        {
            throw new InvalidOperationException(refusal.Message);
        }

        DeliveryOf(version)!.SendAgain();
        RegisterDomainEvent(new TripDispatchedEvent(this, correlationId, sentAt, vehicle, driver, shipments));
    }

    // The version's delivery when a report applies to it: a Sending one. Otherwise why not.
    private TripVersionDelivery? SendingVersion(int version, out TripDeliveryReport report)
    {
        var delivery = DeliveryOf(version);
        report = delivery is null ? TripDeliveryReport.UnknownVersion : TripDeliveryReport.Ignored;
        return delivery is { State: TripDeliveryState.Sending } ? delivery : null;
    }
}
