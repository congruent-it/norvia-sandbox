using System.Diagnostics;

namespace Norvia.Tms.Web.TmsApi;

/// <summary>
/// The TMS UI's own spans, from the activity source <c>ServiceDefaults</c> exports: the one named after the application.
/// </summary>
internal sealed class TmsUiActivities(string applicationName) : IDisposable
{
    public ActivitySource Source { get; } = new(applicationName);

    public void Dispose() => Source.Dispose();
}
