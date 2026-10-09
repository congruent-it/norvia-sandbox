using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Web.Capacity;

/// <summary>How the Capacity view words the Own fleet's Vehicles and Drivers.</summary>
internal static class FleetText
{
    public static string VehicleType(VehicleType type) => type switch
    {
        Domain.Fleet.VehicleType.Van => "Van",
        Domain.Fleet.VehicleType.RigidTruck => "Rigid truck",
        Domain.Fleet.VehicleType.TractorWithTrailer => "Tractor with trailer",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown Vehicle type."),
    };

    public static string Payload(int kg) => $"{kg:N0} kg";

    public static string Licences(IEnumerable<LicenceCategory> licences) => string.Join(", ", licences);

    public static string VehicleTypes(IEnumerable<VehicleType> types) => string.Join(", ", types.Select(VehicleType));
}
