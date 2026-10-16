namespace Norvia.Simulator.Domain.Worlds;

/// <summary>A Driver of the Own fleet, from the seed file, with the licence categories the TMS checks a Vehicle against (P10). The Driver plays their part through the Driver app, signed in as themselves.</summary>
public sealed record SimulatedDriver(string Id, string Name, IReadOnlyList<LicenceCategory> Licences)
{
    /// <summary>True when one of the Driver's licences covers the Vehicle.</summary>
    public bool CanDrive(SimulatedVehicle vehicle)
    {
        ArgumentNullException.ThrowIfNull(vehicle);
        return Licences.Any(held => held >= vehicle.LicenceNeeded);
    }

    /// <summary>The highest category the Driver holds, which is how much the licence is worth keeping for a bigger Vehicle.</summary>
    public LicenceCategory Highest => Licences.Count == 0 ? LicenceCategory.B : Licences.Max();
}
