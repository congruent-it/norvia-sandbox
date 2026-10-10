using Ardalis.SharedKernel;
using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Domain.Trips;

/// <summary>
/// One run on one day by one Vehicle and one Driver (P11). A Draft holds its Vehicle and Driver, so neither has another
/// Trip that day; the rules about who may hold them are P10's, and the handlers apply them before a Draft changes.
/// </summary>
public sealed class Trip : EntityBase<TripId>, IAggregateRoot
{
    private Trip(TripNumber number, DateOnly day, VehicleId? vehicleId, DriverId? driverId)
    {
        Id = TripId.New();
        Number = number;
        Day = day;
        VehicleId = vehicleId;
        DriverId = driverId;
        Status = TripStatus.Draft;
    }

    public TripNumber Number { get; private set; }

    public DateOnly Day { get; private set; }

    public TripStatus Status { get; private set; }

    public VehicleId? VehicleId { get; private set; }

    public DriverId? DriverId { get; private set; }

    /// <summary>A new Draft. Either of the Vehicle and the Driver may be empty.</summary>
    public static Trip CreateDraft(TripNumber number, DateOnly day, VehicleId? vehicleId, DriverId? driverId)
    {
        ArgumentNullException.ThrowIfNull(number);
        return new Trip(number, day, vehicleId, driverId);
    }

    /// <summary>Gives the Draft this Vehicle and Driver, or none, which gives the previous ones back. Returns null when it is done.</summary>
    public TripRefusal? Pick(VehicleId? vehicleId, DriverId? driverId)
    {
        if (EnsureDraft() is { } refusal)
        {
            return refusal;
        }

        VehicleId = vehicleId;
        DriverId = driverId;
        return null;
    }

    /// <summary>Null when the Trip is a Draft, which can be changed or discarded; a Dispatched Trip is neither, and is never deleted.</summary>
    public TripRefusal? EnsureDraft() => Status == TripStatus.Draft ? null : TripRefusal.TripNotDraft(Number);
}
