using Ardalis.SharedKernel;

namespace Norvia.Tms.Domain.Fleet;

/// <summary>
/// A whole day on which a Vehicle can't be used (P10). Like an Absence it is kept apart from the Planning horizon's days,
/// so one can be recorded beyond the horizon and takes effect when the day opens.
/// </summary>
public sealed class OffTheRoadDay : EntityBase<OffTheRoadDayId>, IAggregateRoot
{
    private OffTheRoadDay(VehicleId vehicleId, DateOnly date, OffTheRoadReason reason, string remark)
    {
        Id = OffTheRoadDayId.New();
        VehicleId = vehicleId;
        Date = date;
        Reason = reason;
        Remark = remark;
    }

    public VehicleId VehicleId { get; private set; }

    public DateOnly Date { get; private set; }

    public OffTheRoadReason Reason { get; private set; }

    public string Remark { get; private set; }

    public static OffTheRoadDay Record(VehicleId vehicleId, DateOnly date, OffTheRoadReason reason, string? remark)
    {
        ArgumentNullException.ThrowIfNull(vehicleId);
        return new OffTheRoadDay(vehicleId, date, reason, remark?.Trim() ?? string.Empty);
    }

    /// <summary>Recording the same Vehicle's day again changes its reason and remark.</summary>
    public void Revise(OffTheRoadReason reason, string? remark)
    {
        Reason = reason;
        Remark = remark?.Trim() ?? string.Empty;
    }
}
