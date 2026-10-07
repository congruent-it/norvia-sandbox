using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

namespace Norvia.Tms.Api.IntegrationTests;

internal static class TestAppHost
{
    // Each test run starts a whole AppHost of its own, and what the TMS API's tests don't call only costs startup time
    // and memory. The tests play the P4 booking Logic App themselves, with CarrierReport, so no worker may report on a
    // booking too (ADR 0030), and they record Tracking events themselves, so no worker may read a tracking queue (ADR 0031) or a Proof of delivery queue (ADR 0032).
    private static readonly string[] Unneeded =
    [
        "dbgate", "partner-portal", "partner-portal-migrations", "partner-portal-web", "integration-console-web",
        "partner-api", "booking-worker", "shipper-notification-worker", "carrier-cancellation-worker", "tracking-worker", "proof-of-delivery-worker",
    ];

    /// <summary>Takes the resources the TMS API's tests don't use out of the AppHost, so it starts the TMS (with its UI, which the demo seed test needs), its database and the Service Bus emulator.</summary>
    public static void RemoveWhatTheTmsApiTestsDontUse(this IDistributedApplicationTestingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        foreach (var resource in builder.Resources
            .Where(resource => Unneeded.Any(name => resource.Name == name || resource.Name == $"{name}-rebuilder"))
            .ToList())
        {
            builder.Resources.Remove(resource);
        }
    }
}
