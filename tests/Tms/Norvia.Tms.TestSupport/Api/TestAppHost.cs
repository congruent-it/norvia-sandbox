using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

namespace Norvia.Tms.TestSupport;

public static class TestAppHost
{
    // Each test run starts a whole AppHost of its own, and what the TMS API's tests don't call only costs startup time
    // and memory. The tests play the P4 booking Logic App themselves, with CarrierReport, so no worker may report on a
    // booking too (ADR 0030), and they report a Trip's delivery themselves, so no worker may deliver a Trip (P11) or read a Driver's report (P12), nothing here bills or invoices (the E1 Billing and Shipper invoice workers; the latter waits for the Partner portal, which is gone), and they record Tracking events themselves, so no worker may read a tracking queue (ADR 0031) or a Proof of delivery queue (ADR 0032).
    private static readonly string[] Unneeded =
    [
        "dbgate", "partner-portal", "partner-portal-migrations", "partner-portal-web", "integration-console-web",
        "partner-api", "booking-worker", "shipper-notification-worker", "carrier-cancellation-worker", "trip-dispatch-worker", "driver-reports-worker", "tracking-worker", "proof-of-delivery-worker",
        "billing-worker", "shipper-invoice-worker", "carrier-invoice-outcome-worker",
        "simulator", "simulator-migrations", "simulator-web",
    ];

    // The TMS UI's journeys keep the Partner portal and the workers that play the booking, notification, cancellation and Trip dispatch Logic Apps,
    // with the Driver app API a dispatched Trip goes to. They record Tracking events and Proofs of delivery through the TMS API themselves, so
    // the tracking and Proof of delivery workers, the Driver reports worker, the Driver app's screens, the Integration console and the Simulator only cost startup time and memory.
    private static readonly string[] UnneededByTheTmsUi =
    [
        "dbgate", "driver-app-web", "integration-console-web", "driver-reports-worker", "tracking-worker", "proof-of-delivery-worker",
        "simulator", "simulator-migrations", "simulator-web",
    ];

    /// <summary>Takes the resources the TMS UI's tests don't use out of the AppHost, so it starts the TMS, its UI, the Partner portal, the workers that play the Logic Apps the journeys go through, and the Driver app API.</summary>
    public static void RemoveWhatTheTmsUiTestsDontUse(this IDistributedApplicationTestingBuilder builder) => builder.Remove(UnneededByTheTmsUi);

    /// <summary>
    /// Takes the resources the Customer service agent's UI tests don't use out of the AppHost. They keep what the TMS UI's journeys use and the MCP stand-in that plays API Management's MCP
    /// server, with the Integration console, which the stand-in waits for.
    /// </summary>
    public static void RemoveWhatTheCustomerServiceAgentUiTestsDontUse(this IDistributedApplicationTestingBuilder builder) =>
        builder.Remove([.. UnneededByTheTmsUi.Where(name => name is not "integration-console-web")]);

    /// <summary>
    /// Takes the resources the fault tests don't use out of the AppHost. They keep the workers that play the Carrier-tracking and Driver reports Logic Apps (and the Trip dispatch
    /// Logic App that gives the Driver app its Trips), as each fault plays through the real path, from the Driver or the queue to the TMS.
    /// </summary>
    public static void RemoveWhatTheTmsFaultTestsDontUse(this IDistributedApplicationTestingBuilder builder) =>
        builder.Remove([.. Unneeded.Where(name => name is not ("trip-dispatch-worker" or "driver-reports-worker" or "tracking-worker"))]);

    /// <summary>
    /// Takes the resources the fault UI tests don't use out of the AppHost. They keep what a Driver's report and a Shipper's screen go through: the Partner portal (its presenter panel switches the faults),
    /// the Driver app with its screens, the workers that play the Trip dispatch and Driver reports Logic Apps, and the TMS with its UI.
    /// </summary>
    public static void RemoveWhatTheTmsFaultUiTestsDontUse(this IDistributedApplicationTestingBuilder builder) =>
        builder.Remove(["dbgate", "integration-console-web", "tracking-worker", "proof-of-delivery-worker", "simulator", "simulator-migrations", "simulator-web"]);

    /// <summary>
    /// Takes the resources the Customer service agent's tests don't use out of the AppHost. They keep the TMS (API and UI), its database, the MCP stand-in that plays API Management's
    /// MCP server, and the Integration console, which the stand-in waits for and serves its first server from.
    /// </summary>
    public static void RemoveWhatTheCustomerServiceAgentTestsDontUse(this IDistributedApplicationTestingBuilder builder) =>
        builder.Remove([.. Unneeded.Where(name => name is not "integration-console-web")]);

    /// <summary>Takes the resources the TMS API's tests don't use out of the AppHost, so it starts the TMS (with its UI, which the demo seed test needs), its database and the Service Bus emulator.</summary>
    public static void RemoveWhatTheTmsApiTestsDontUse(this IDistributedApplicationTestingBuilder builder) => builder.Remove(Unneeded);

    private static void Remove(this IDistributedApplicationTestingBuilder builder, string[] unneeded)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // The Estate health check reads the Integration console's address, so without the console it would stay Starting for ever, and with it the start of the AppHost.
        if (unneeded.Contains("integration-console-web"))
        {
            unneeded = [.. unneeded, "health-check-web"];
        }

        foreach (var resource in builder.Resources
            .Where(resource => unneeded.Any(name => resource.Name == name || resource.Name == $"{name}-rebuilder"))
            .ToList())
        {
            builder.Resources.Remove(resource);
        }
    }
}
