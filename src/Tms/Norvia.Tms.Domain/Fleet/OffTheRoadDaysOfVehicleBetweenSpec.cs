using Ardalis.Specification;

namespace Norvia.Tms.Domain.Fleet;

/// <summary>A Vehicle's Off the road days between two dates, to change in place.</summary>
public sealed class OffTheRoadDaysOfVehicleBetweenSpec : Specification<OffTheRoadDay>
{
    public OffTheRoadDaysOfVehicleBetweenSpec(VehicleId vehicleId, DateOnly from, DateOnly to)
    {
        Query.Where(day => day.VehicleId == vehicleId && day.Date >= from && day.Date <= to);
    }
}
