using System.Text.Json.Nodes;

/// <summary>
/// Gives every subscription on the local Service Bus emulator the SQL filter the tracking contract requires
/// (coding guidelines §7), so a message resubmitted from the Integration console reaches only the subscription it was
/// dead-lettered from. Aspire's subscription rules only model correlation filters, so the filter goes straight into
/// the emulator's configuration.
/// </summary>
internal static class ResubmitAwareFilters
{
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
                            ["SqlExpression"] = $"resubmitTo IS NULL OR resubmitTo = '{name}'",
                        },
                    },
                },
            };
        }
    }
}
