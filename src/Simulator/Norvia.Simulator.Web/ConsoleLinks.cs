namespace Norvia.Simulator.Web;

/// <summary>
/// Where the Integration console is, so a log line can open the Message trail of its correlation ID there. The console's search
/// takes the key in its address (<c>/?key=…</c>), over everything the estate keeps.
/// </summary>
public sealed class ConsoleLinks(Uri? address)
{
    /// <summary>The console's search for a correlation ID, or null when the console's address isn't known.</summary>
    public string? Trail(string correlationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        return address is null ? null : $"{address.ToString().TrimEnd('/')}/?key={Uri.EscapeDataString(correlationId)}&within=all";
    }
}
