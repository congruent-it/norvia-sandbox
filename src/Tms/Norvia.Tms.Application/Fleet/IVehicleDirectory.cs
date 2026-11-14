using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Application.Fleet;

/// <summary>The TMS's read copy of Vehicle master data, which the ERP owns (ADR 0014).</summary>
public interface IVehicleDirectory
{
    /// <summary>The active Vehicles, by registration.</summary>
    IReadOnlyList<Vehicle> ListActive();

    /// <summary>The copy of a Vehicle, active or not, or null when the TMS has never heard of it.</summary>
    Vehicle? Find(VehicleId id);
}
