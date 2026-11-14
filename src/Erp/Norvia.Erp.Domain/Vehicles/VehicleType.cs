namespace Norvia.Erp.Domain.Vehicles;

/// <summary>The kinds of Vehicle in the Own fleet, named as the TMS names them.</summary>
public enum VehicleType
{
    /// <summary>A van, up to 3.5 t.</summary>
    Van,

    RigidTruck,

    /// <summary>A tractor with its trailer, treated as one Vehicle.</summary>
    TractorWithTrailer,
}
