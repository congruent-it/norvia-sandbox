using Ardalis.SharedKernel;

namespace Norvia.Tms.Domain.Fleet;

/// <summary>
/// A whole day on which a Driver can't work (P10). It is kept apart from the Planning horizon's days, so the Planner can
/// record one for a day beyond the horizon and the horizon job honours it when it opens that day.
/// </summary>
public sealed class Absence : EntityBase<AbsenceId>, IAggregateRoot
{
    private Absence(DriverId driverId, DateOnly date, AbsenceReason reason, string remark)
    {
        Id = AbsenceId.New();
        DriverId = driverId;
        Date = date;
        Reason = reason;
        Remark = remark;
    }

    public DriverId DriverId { get; private set; }

    public DateOnly Date { get; private set; }

    public AbsenceReason Reason { get; private set; }

    public string Remark { get; private set; }

    public static Absence Record(DriverId driverId, DateOnly date, AbsenceReason reason, string? remark)
    {
        ArgumentNullException.ThrowIfNull(driverId);
        return new Absence(driverId, date, reason, remark?.Trim() ?? string.Empty);
    }

    /// <summary>Recording the same Driver's day again changes its reason and remark.</summary>
    public void Revise(AbsenceReason reason, string? remark)
    {
        Reason = reason;
        Remark = remark?.Trim() ?? string.Empty;
    }
}
