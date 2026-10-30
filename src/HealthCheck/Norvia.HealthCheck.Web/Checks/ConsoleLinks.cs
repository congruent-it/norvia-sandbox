namespace Norvia.HealthCheck.Web.Checks;

/// <summary>Links to the Integration console, from its address in configuration (<c>IntegrationConsole:Address</c>).</summary>
public sealed class ConsoleLinks(Uri? address)
{
    /// <summary>The console's search for a correlation ID over all time, or null when the console's address isn't known.</summary>
    public string? Trail(string correlationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        return address is null ? null : $"{address.ToString().TrimEnd('/')}/?key={Uri.EscapeDataString(correlationId)}&within=all";
    }
}
