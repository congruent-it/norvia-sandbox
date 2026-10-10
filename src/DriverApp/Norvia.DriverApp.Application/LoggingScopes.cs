using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Norvia.DriverApp.Application;

public static class LoggingScopes
{
    /// <summary>
    /// Puts the tracking-contract keys in the logging scope and on the request's trace (coding guidelines §7), so every
    /// trace of a step joins its Message trail. The scope lasts until the result is disposed.
    /// </summary>
    public static IDisposable? Track(this ILogger logger, Dictionary<string, object?> keys)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(keys);
        foreach (var (key, value) in keys)
        {
            Activity.Current?.SetTag(key, value);
        }

        return logger.BeginScope(keys);
    }
}
