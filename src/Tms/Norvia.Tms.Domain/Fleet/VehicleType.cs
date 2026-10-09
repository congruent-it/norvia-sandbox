namespace Norvia.Tms.Domain.Fleet;

/// <summary>The kinds of Vehicle in the Own fleet.</summary>
public enum VehicleType
{
    /// <summary>A van, up to 3.5 t.</summary>
    Van,

    RigidTruck,

    /// <summary>A tractor with its trailer, treated as one Vehicle.</summary>
    TractorWithTrailer,
}
