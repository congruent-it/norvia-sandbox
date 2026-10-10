using Ardalis.SharedKernel;
using Norvia.Tms.Domain.Fleet;
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

    public TripNumber Number { get; private set; }

    public DateOnly Day { get; private set; }

    public TripStatus Status { get; private set; }

    public VehicleId? VehicleId { get; private set; }

    public DriverId? DriverId { get; private set; }

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

    /// <summary>Null when the Trip is a Draft, which can be changed or discarded; a Dispatched Trip is neither, and is never deleted.</summary>
    public TripRefusal? EnsureDraft() => Status == TripStatus.Draft ? null : TripRefusal.TripNotDraft(Number);
}
