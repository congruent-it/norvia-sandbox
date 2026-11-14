using Norvia.Erp.Domain.Drivers;
using Norvia.Erp.Domain.Vehicles;

namespace Norvia.Erp.Application.MasterData;

/// <summary>The values Finance picks from in the master-data forms, named as the ERP API takes them.</summary>
public static class MasterDataChoices
{
    public static IReadOnlyList<string> VehicleTypes { get; } = [.. Enum.GetNames<VehicleType>()];

    public static IReadOnlyList<string> LicenceCategories { get; } = [.. Enum.GetNames<LicenceCategory>()];

    /// <summary>The type a new Vehicle starts as.</summary>
    public static string DefaultVehicleType => nameof(VehicleType.Van);
}
