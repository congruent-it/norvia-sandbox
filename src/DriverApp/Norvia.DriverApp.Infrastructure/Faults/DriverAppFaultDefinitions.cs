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
}
