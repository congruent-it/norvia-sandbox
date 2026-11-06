using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Norvia.Agents.Scripts;

/// <summary>
/// The scripted model's summary of a Transport Order for the Customer service agent: it reads the order, and, when the order has a
/// Shipment, the Shipment's timeline, Exceptions and Proof of delivery status, then tells the order's life in one paragraph, each
/// claim citing the result it came from, and what blocks it, if anything. It states nothing a result did not show.
/// </summary>
internal static class CustomerServiceOrderScript
{
    public const string GetTransportOrder = "get_transport_order";
    public const string GetShipmentTimeline = "get_shipment_timeline";
    public const string GetExceptions = "get_exceptions";
    public const string GetProofOfDeliveryStatus = "get_proof_of_delivery_status";

    public static IReadOnlyList<AIContent> Next(ScriptContext context, string number)
    {
        if (context.Results.Count == 0)
        {
            return [Call(context, 0, GetTransportOrder, "transportOrderNumber", number)];
        }

        var order = Parse(context.Results[0].Content);
        if (context.Results.Count == 1 && Text(order, "shipmentNumber") is { } shipment)
        {
            return
            [
                Call(context, 1, GetShipmentTimeline, "shipmentNumber", shipment),
                Call(context, 2, GetExceptions, "shipmentNumber", shipment),
                Call(context, 3, GetProofOfDeliveryStatus, "shipmentNumber", shipment),
            ];
        }

        return [new TextContent(Answer(number, context.Results))];
    }

    private static FunctionCallContent Call(ScriptContext context, int index, string tool, string argument, string value) =>
        new($"call-{context.Turn}-{index}", tool, new Dictionary<string, object?> { [argument] = value });

    private static string Answer(string number, IReadOnlyList<ScriptedToolResult> results)
    {
        var order = results[0];
        var orderJson = Parse(order.Content);
        if (Text(orderJson, "status") is not { } status)
        {
            return IntegrationOperationsScript.Json(
                $"The result shows nothing about Transport Order {number}, so its life cannot be summarised from it.",
                [("The Transport Order came back without a status.", order.Id)],
                "Ask a colleague to look at the Transport Order in the TMS.",
                "low",
                "needs-a-person");
        }

        var sentences = new List<string>();
        var evidence = new List<(string, string)>();
        var blockers = new List<string>();

        sentences.Add($"Transport Order {number} is {Words(status)}.");
        evidence.Add(($"The Transport Order's status is {status}.", order.Id));

        // Rating
        if (Object(orderJson, "rating") is { } rating)
        {
            var ratingResult = Text(rating, "result") ?? "Unknown";
            if (ratingResult == "RatingFailed")
            {
                var code = Text(rating, "failureCode") ?? "an unknown reason";
                sentences.Add($"Rating failed with {code}.");
                evidence.Add(($"Rating failed with {code}: {Text(rating, "failureMessage") ?? "no message"}", order.Id));
                blockers.Add($"Rating failed ({code}); a person has to fix the Rate Card or the lane and re-rate it.");
            }
            else
            {
                sentences.Add($"Its rating result is {Words(ratingResult)}.");
                evidence.Add(($"The rating result is {ratingResult}.", order.Id));
            }
        }

        // Planning and tracking
        var shipmentNumber = Text(orderJson, "shipmentNumber");
        if (shipmentNumber is null)
        {
            sentences.Add("It has no Shipment yet, so it has not been planned.");
            evidence.Add(("The Transport Order has no Shipment.", order.Id));
        }
        else
        {
            sentences.Add($"Shipment {shipmentNumber} is {Words(Text(orderJson, "shipmentStatus") ?? "of unknown status")}.");
            evidence.Add(($"Shipment {shipmentNumber} has the status {Text(orderJson, "shipmentStatus") ?? "unknown"}.", order.Id));
            AddTracking(results, sentences, evidence);
            AddProofOfDelivery(results, sentences, evidence, blockers);
            AddExceptions(results, sentences, evidence, blockers);
        }

        // Billing
        if (Object(orderJson, "billing") is { } billing)
        {
            var state = Text(billing, "state") ?? "Unknown";
            sentences.Add($"For billing it is {Words(state)}.");
            evidence.Add(($"The billing state is {state}.", order.Id));
            if (billing.TryGetProperty("blockingReasons", out var reasons) && reasons.ValueKind == JsonValueKind.Array)
            {
                foreach (var reason in reasons.EnumerateArray().Where(reason => reason.ValueKind == JsonValueKind.String))
                {
                    var name = reason.GetString()!;
                    evidence.Add(($"Billing is blocked by {name}.", order.Id));
                    blockers.Add(BlockerText(name));
                }
            }
        }

        var blocking = blockers.Distinct().ToList();
        sentences.Add(blocking.Count == 0 ? "Nothing is blocking it." : $"It is blocked: {string.Join(" ", blocking)}");
        return IntegrationOperationsScript.Json(
            string.Join(" ", sentences),
            evidence,
            blocking.Count == 0 ? "Nothing needs doing for this Transport Order." : string.Join(" ", blocking),
            "high",
            "needs-a-person");
    }

    private static void AddTracking(IReadOnlyList<ScriptedToolResult> results, List<string> sentences, List<(string, string)> evidence)
    {
        if (Find(results, GetShipmentTimeline) is not { } result)
        {
            return;
        }

        var events = Array(Parse(result.Content), "trackingEvents");
        if (events.Count == 0)
        {
            sentences.Add("No Carrier or Driver has reported anything on it yet.");
            evidence.Add(("The timeline holds no Tracking events.", result.Id));
            return;
        }

        var last = events[^1];
        var source = Text(last, "source") ?? "an unknown source";
        sentences.Add($"It has {events.Count} Tracking event{(events.Count == 1 ? string.Empty : "s")}; the last is {Words(Text(last, "type") ?? "unknown")} at {Text(last, "place") ?? "an unknown place"}, reported by {source}.");
        evidence.Add(($"The last of {events.Count} Tracking events is {Text(last, "type")} at {Text(last, "place")}, reported by {source}.", result.Id));
    }

    private static void AddProofOfDelivery(IReadOnlyList<ScriptedToolResult> results, List<string> sentences, List<(string, string)> evidence, List<string> blockers)
    {
        if (Find(results, GetProofOfDeliveryStatus) is not { } result)
        {
            return;
        }

        var json = Parse(result.Content);
        var status = Text(json, "status") ?? "Unknown";
        var reservations = json.ValueKind == JsonValueKind.Object && json.TryGetProperty("withReservations", out var flag) && flag.ValueKind == JsonValueKind.True;
        var superseded = Array(json, "proofsOfDelivery").Count(proof => !(proof.TryGetProperty("isCurrent", out var current) && current.ValueKind == JsonValueKind.True));
        sentences.Add(status switch
        {
            "Current" => reservations ? "Its Proof of delivery is in, taken with reservations." : "Its Proof of delivery is in and was taken clean.",
            "Missing" => "Its Proof of delivery is missing.",
            "NotDueYet" => "It is delivered and its Proof of delivery is not overdue yet.",
            "NotDelivered" => "It has not been delivered, so no Proof of delivery is due.",
            _ => "The state of its Proof of delivery is unknown.",
        } + (superseded > 0 ? $" {superseded} earlier Proof{(superseded == 1 ? string.Empty : "s")} of delivery {(superseded == 1 ? "was" : "were")} superseded." : string.Empty));
        evidence.Add(($"The Proof of delivery status is {status}{(reservations ? ", with reservations" : string.Empty)}.", result.Id));
        if (status == "Missing")
        {
            blockers.Add("The Proof of delivery is missing: ask the Carrier for it, or release the order without one under your own role.");
        }
    }

    private static void AddExceptions(IReadOnlyList<ScriptedToolResult> results, List<string> sentences, List<(string, string)> evidence, List<string> blockers)
    {
        if (Find(results, GetExceptions) is not { } result)
        {
            return;
        }

        var exceptions = Array(Parse(result.Content), "exceptions");
        if (exceptions.Count == 0)
        {
            sentences.Add("It has had no Exceptions.");
            evidence.Add(("The Shipment has no Exceptions.", result.Id));
            return;
        }

        var open = exceptions.Where(exception => Text(exception, "status") == "Open").ToList();
        var notified = exceptions.Count(exception => exception.TryGetProperty("shipperNotified", out var flag) && flag.ValueKind == JsonValueKind.True);
        sentences.Add(
            $"It has had {exceptions.Count} Exception{(exceptions.Count == 1 ? string.Empty : "s")} ({string.Join(", ", exceptions.Select(exception => Words(Text(exception, "type") ?? "unknown")))}), {open.Count} still open; the Shipper was notified of {notified}.");
        evidence.Add(($"There are {exceptions.Count} Exceptions, {open.Count} open, and the Shipper was notified of {notified}.", result.Id));
        foreach (var exception in open)
        {
            blockers.Add($"The {Words(Text(exception, "type") ?? "unknown")} Exception {Text(exception, "exceptionNumber")} is still open: the Planner has to resolve it.");
        }
    }

    private static string BlockerText(string reason) => reason switch
    {
        "NotRated" => "It is not rated, so it cannot be billed.",
        "NoProofOfDelivery" => "It has no Proof of delivery.",
        "OpenException" => "An Exception on its Shipment is open.",
        _ => $"Billing is blocked by {reason}.",
    };

    private static ScriptedToolResult? Find(IReadOnlyList<ScriptedToolResult> results, string tool) => results.FirstOrDefault(result => result.Tool == tool);

    // "NotYetBillable" -> "not yet billable"
    private static string Words(string name) =>
        System.Text.RegularExpressions.Regex.Replace(name, "(?<=[a-z])(?=[A-Z])", " ").ToLowerInvariant();

    private static JsonElement Parse(string json)
    {
        try
        {
            return JsonDocument.Parse(json).RootElement.Clone();
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object ? IntegrationOperationsScript.Text(element, property) : null;

    private static JsonElement? Object(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Object ? value : null;

    private static List<JsonElement> Array(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Array
            ? [.. value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object)]
            : [];
}
