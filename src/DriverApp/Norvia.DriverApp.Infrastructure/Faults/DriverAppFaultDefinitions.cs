using Norvia.DriverApp.Application.Faults;
using Norvia.ServiceDefaults.Faults;

namespace Norvia.DriverApp.Infrastructure.Faults;

internal static class DriverAppFaultDefinitions
{
    public const string Owner = "driver-app";

    public static readonly FaultDefinition Unavailable = new(
        DriverAppFaultNames.Unavailable,
        Owner,
        "The Driver app refuses every Trip the Trip dispatch Logic App sends, with a 503.");

    public static readonly FaultDefinition HeicPhoto = new(
        DriverAppFaultNames.HeicPhoto,
        Owner,
        "The Driver app sends a photo of a Proof of delivery as HEIC: the TMS refuses the Proof of delivery with FileNotAccepted and the Driver sees it Refused.");

    public static readonly FaultDefinition[] All = [Unavailable, HeicPhoto];
}
