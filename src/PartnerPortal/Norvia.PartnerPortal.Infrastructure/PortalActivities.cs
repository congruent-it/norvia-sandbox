using System.Diagnostics;

namespace Norvia.PartnerPortal.Infrastructure;

/// <summary>
/// The portal's own spans, from the activity source <c>ServiceDefaults</c> exports: the one named after the application.
/// </summary>
internal sealed class PortalActivities(string applicationName) : IDisposable
{
    public ActivitySource Source { get; } = new(applicationName);

    public void Dispose() => Source.Dispose();
}
