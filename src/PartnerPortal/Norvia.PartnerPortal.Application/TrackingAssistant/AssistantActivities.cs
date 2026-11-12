using System.Diagnostics;

namespace Norvia.PartnerPortal.Application.TrackingAssistant;

/// <summary>The portal's span source for an assistant run: the one named after the application, which <c>ServiceDefaults</c> exports (coding guidelines §7).</summary>
public sealed class AssistantActivities(string applicationName) : IDisposable
{
    public ActivitySource Source { get; } = new(applicationName);

    public void Dispose() => Source.Dispose();
}
