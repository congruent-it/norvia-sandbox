using System.Diagnostics;

namespace Norvia.Erp.Web.ShipperInvoices;

/// <summary>
/// The ERP UI's source of spans, named after the application, which <c>ServiceDefaults</c> exports (coding guidelines §7). A command
/// Finance sends from the UI runs in a span of its own, which is the UI's hop on the trail.
/// </summary>
internal sealed class ErpActivities(IHostEnvironment environment) : IDisposable
{
    public ActivitySource Source { get; } = new(environment.ApplicationName);

    public void Dispose() => Source.Dispose();
}
