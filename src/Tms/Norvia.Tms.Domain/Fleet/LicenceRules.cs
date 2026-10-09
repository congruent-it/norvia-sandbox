namespace Norvia.Tms.Domain.Fleet;

/// <summary>Which licence category each Vehicle type needs, and which categories cover which (P10).</summary>
public static class LicenceRules
{
    /// <summary>The licence category a Driver needs to drive this Vehicle type.</summary>
    public static LicenceCategory Needed(VehicleType type) => type switch
    {
        VehicleType.Van => LicenceCategory.B,
        VehicleType.RigidTruck => LicenceCategory.C,
        VehicleType.TractorWithTrailer => LicenceCategory.CE,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown Vehicle type."),
    };

    /// <summary>True when a licence in the held category lets its holder drive what the needed category allows: CE covers C, and C covers B.</summary>
    public static bool Covers(LicenceCategory held, LicenceCategory needed) => held >= needed;
}
