using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Application.Fleet;

/// <summary>The TMS's read copy of Driver master data, which the ERP owns (ADR 0014).</summary>
public interface IDriverDirectory
{
    /// <summary>The active Drivers, by name.</summary>
    IReadOnlyList<Driver> ListActive();

    /// <summary>The copy of a Driver, active or not, or null when the TMS has never heard of it.</summary>
    Driver? Find(DriverId id);
}
