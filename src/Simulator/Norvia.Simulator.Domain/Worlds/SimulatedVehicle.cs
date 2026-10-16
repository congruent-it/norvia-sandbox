namespace Norvia.Simulator.Domain.Worlds;

/// <summary>
/// A Vehicle of the Own fleet, from the seed file. Whether it is available on a day is the TMS's to say (P10); the Simulator
/// knows its capacity, so it doesn't put on a Trip what the Vehicle can't carry.
/// </summary>
public sealed record SimulatedVehicle(string Id, string Registration, VehicleKind Kind, int PayloadKg, int PalletPlaces)
{
    /// <summary>The licence category a Driver needs to drive this Vehicle.</summary>
    public LicenceCategory LicenceNeeded => Kind switch
    {
        VehicleKind.Van => LicenceCategory.B,
        VehicleKind.RigidTruck => LicenceCategory.C,
        VehicleKind.TractorWithTrailer => LicenceCategory.CE,
        _ => throw new InvalidOperationException($"Unknown Vehicle kind {Kind}."),
    };
}
