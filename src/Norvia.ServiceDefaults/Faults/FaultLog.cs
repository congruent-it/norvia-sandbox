using Microsoft.Extensions.Logging;

namespace Norvia.ServiceDefaults.Faults;

internal static partial class FaultLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Fault {Fault} switched on until {Until}")]
    public static partial void SwitchedOn(ILogger logger, string fault, DateTimeOffset? until);

    [LoggerMessage(Level = LogLevel.Information, Message = "Fault {Fault} switched off")]
    public static partial void SwitchedOff(ILogger logger, string fault);
}
