using System.Text.Json.Nodes;

/// <summary>
/// Gives every subscription on the local Service Bus emulator the SQL filter the tracking contract requires
/// (coding guidelines §7), so a message resubmitted from the Integration console reaches only the subscription it was
/// dead-lettered from. Aspire's subscription rules only model correlation filters, so the filter goes straight into
/// the emulator's configuration.
/// </summary>
internal static class ResubmitAwareFilters
{
    /// <summary>
    /// The conditions of the subscriptions that take only some of their topic's messages, by subscription name. The
    /// estate Bicep gives the same subscriptions the same conditions.
    /// </summary>
    private static readonly Dictionary<string, string> Conditions = new()
    {
        ["carrier-booking"] = "assignedTo = 'Carrier'",
        ["shipper-notification"] = "eventType = 'ShipperNotificationRequested'",
        ["carrier-cancellation"] = "eventType = 'CarrierBookingCancelled' OR eventType = 'CarrierBookingWithdrawn'",
        ["shipper-invoice"] = "eventType = 'ShipperInvoiceIssued'",
        ["billing"] = "eventType = 'TransportOrderBillable'",
        ["trip-dispatch"] = "eventType = 'TripDispatched'",
        ["driver-reports"] = "eventType = 'TripStartReported' OR eventType = 'TripEndReported' OR eventType = 'StopCompletedReported' OR eventType = 'ProofOfDeliveryCaptured'",
    };

    public static void Apply(JsonNode configuration)
    {
        var subscriptions = configuration["UserConfig"]?["Namespaces"]?.AsArray()
            .SelectMany(ns => ns?["Topics"]?.AsArray() ?? [])
            .SelectMany(topic => topic?["Subscriptions"]?.AsArray() ?? [])
            .OfType<JsonObject>()
            .ToList() ?? [];

        if (subscriptions.Count == 0)
        {
            throw new InvalidOperationException("Found no subscriptions in the Service Bus emulator configuration to filter.");
        }

        foreach (var subscription in subscriptions)
        {
            var name = subscription["Name"]!.GetValue<string>();
            subscription["Rules"] = new JsonArray
            {
                new JsonObject
                {
                    ["Name"] = "resubmit-aware",
                    ["Properties"] = new JsonObject
                    {
                        ["FilterType"] = "Sql",
                        ["SqlFilter"] = new JsonObject
                        {
                            ["SqlExpression"] = Conditions.TryGetValue(name, out var condition)
                                ? $"({condition} AND resubmitTo IS NULL) OR resubmitTo = '{name}'"
                                : $"resubmitTo IS NULL OR resubmitTo = '{name}'",
                        },
                    },
                },
            };
        }
    }
}
