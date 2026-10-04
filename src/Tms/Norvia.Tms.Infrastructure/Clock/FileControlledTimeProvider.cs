using System.Globalization;

namespace Norvia.Tms.Infrastructure.Clock;

/// <summary>
/// A clock a test host controls from outside the process: the instant in a file, read on every call, and the system's own
/// time while the file doesn't exist. The TMS runs as a process of its own under Aspire's test host, so a test sets its
/// time by writing the file. Only a development host registers it (see <see cref="DependencyInjection"/>).
/// Timers are the system's: only what the TMS reads as "now" is controlled.
/// </summary>
internal sealed class FileControlledTimeProvider(string path) : TimeProvider
{
    public override DateTimeOffset GetUtcNow()
    {
        try
        {
            return DateTimeOffset.Parse(File.ReadAllText(path).Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
        }
        catch (FileNotFoundException)
        {
            return base.GetUtcNow();
        }
        catch (DirectoryNotFoundException)
        {
            return base.GetUtcNow();
        }
    }
}
