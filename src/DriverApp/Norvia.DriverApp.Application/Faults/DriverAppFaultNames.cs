namespace Norvia.DriverApp.Application.Faults;

/// <summary>The names of the faults the Driver app owns. The presenter panel switches them, and a hop one caused carries the name as its <c>fault</c> key.</summary>
public static class DriverAppFaultNames
{
    /// <summary>While on, the Driver app refuses every Trip version with a 503.</summary>
    public const string Unavailable = "driver-app-unavailable";

    /// <summary>P12: while on, a Proof of delivery carries a <c>.heic</c> photo, which the TMS refuses.</summary>
    public const string HeicPhoto = "driver-app-heic-photo";
}
