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
    public const string GetReleaseEligibility = "get_release_eligibility";

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

    /// <summary>Rating failed: one read of the order, then the cause and what fixes it. The Rate Card is not Customer service's to edit.</summary>
    public static IReadOnlyList<AIContent> NextRatingFailure(ScriptContext context, string number) =>
        context.Results.Count == 0
            ? [Call(context, 0, GetTransportOrder, "transportOrderNumber", number)]
            : [new TextContent(RatingFailureAnswer(number, context.Results[0]))];

    /// <summary>Not yet billable: one read of the order, then what holds it back and which of that Customer service can act on.</summary>
    public static IReadOnlyList<AIContent> NextNotYetBillable(ScriptContext context, string number) =>
        context.Results.Count == 0
            ? [Call(context, 0, GetTransportOrder, "transportOrderNumber", number)]
            : [new TextContent(NotYetBillableAnswer(number, context.Results[0]))];

    /// <summary>
    /// Release without a Proof of delivery: the TMS decides eligibility (get_release_eligibility), and only an eligible order is weighed
    /// further, on how long it has been Delivered and whether the Carrier has sent a Proof of delivery meanwhile. It never drafts the remark.
    /// </summary>
    public static IReadOnlyList<AIContent> NextRelease(ScriptContext context, string number)
    {
        if (context.Results.Count == 0)
        {
            return [Call(context, 0, GetReleaseEligibility, "transportOrderNumber", number)];
        }

        var eligibility = Parse(context.Results[0].Content);
        if (context.Results.Count == 1 && Flag(eligibility, "eligible") && Text(eligibility, "shipmentNumber") is { } shipment)
        {
            return [Call(context, 1, GetProofOfDeliveryStatus, "shipmentNumber", shipment)];
        }

        return [new TextContent(ReleaseAnswer(number, context.Results))];
    }

    private static string ReleaseAnswer(string number, IReadOnlyList<ScriptedToolResult> results)
    {
        var result = results[0];
        var json = Parse(result.Content);
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty("eligible", out _))
        {
            return IntegrationOperationsScript.Json(
                $"The result shows nothing about whether Transport Order {number} may be released, so nothing can be recommended.",
                [("The release eligibility came back without an answer.", result.Id)],
                "Ask a colleague to look at the Transport Order in the TMS.",
                "low",
                "needs-a-person");
        }

        if (!Flag(json, "eligible"))
        {
            var reasons = json.TryGetProperty("blockingReasons", out var listed) && listed.ValueKind == JsonValueKind.Array
                ? listed.EnumerateArray().Where(reason => reason.ValueKind == JsonValueKind.String).Select(reason => reason.GetString()!).ToList()
                : [];
            if (reasons.Count == 0)
            {
                return IntegrationOperationsScript.Json(
                    $"The TMS says Transport Order {number} may not be released without a Proof of delivery, but gives no reason.",
                    [("The release eligibility is false and lists no blocking reason.", result.Id)],
                    "Do not release it. Ask a colleague to look at the Transport Order in the TMS.",
                    "low",
                    "needs-a-person");
            }

            return IntegrationOperationsScript.Json(
                $"Transport Order {number} cannot be released without a Proof of delivery now: {string.Join(" and ", reasons.Select(ReleaseBlockerText))}.",
                reasons.Select(reason => ($"The TMS lists {reason} as blocking the release.", result.Id)),
                reasons.Contains("OpenException") || reasons.Contains("NotRated")
                    ? "Do not release it. " + string.Join(" ", reasons.Select(ReleaseBlockerAdvice).OfType<string>())
                    : "Do not release it: the TMS does not allow it.",
                "high",
                "needs-a-person");
        }

        var evidence = new List<(string, string)> { ("The TMS says the order may be released without a Proof of delivery and nothing else blocks it.", result.Id) };
        var delivered = Text(json, "deliveredAt");
        var asOf = Text(json, "asOf");
        var status = results.Count > 1 ? Text(Parse(results[1].Content), "status") : null;
        var since = delivered is not null && asOf is not null && DateTimeOffset.TryParse(delivered, out var at) && DateTimeOffset.TryParse(asOf, out var now)
            ? $"It was delivered {(int)Math.Max(0, (now - at).TotalDays)} day(s) ago. "
            : string.Empty;
        if (results.Count > 1)
        {
            evidence.Add(($"The Proof of delivery status is {status ?? "unknown"}.", results[1].Id));
        }

        // Carriers' lateness with Proofs of delivery is not exposed by the TMS, so it is not weighed.
        return status switch
        {
            "Missing" => IntegrationOperationsScript.Json(
                $"{since}No Carrier has sent a Proof of delivery within the time the TMS allows, and nothing else blocks Transport Order {number}.",
                evidence,
                "Release it without a Proof of delivery once you have what you rely on instead. You write the remark yourself and confirm the release.",
                "medium",
                "release"),
            "NotDueYet" => IntegrationOperationsScript.Json(
                $"{since}The Carrier still has time to send a Proof of delivery for Transport Order {number}.",
                evidence,
                "Wait. Look again when the TMS lists the Proof of delivery as missing, and ask the Carrier for it meanwhile.",
                "medium",
                "wait"),
            _ => IntegrationOperationsScript.Json(
                $"{since}The Proof of delivery status of Transport Order {number} does not say whether the Carrier is late with it.",
                evidence,
                "Look at the Shipment yourself before you decide whether to release it.",
                "low",
                "needs-a-person"),
        };
    }

    private static string ReleaseBlockerText(string reason) => reason switch
    {
        "AlreadyBillable" => "it is Billable already",
        "NotBilledSeparately" => "it is billed through the Fulfilment Contract",
        "ShipmentNotDelivered" => "its Shipment is not Delivered",
        "AlreadyReleased" => "it was released already",
        "ProofOfDeliveryReceived" => "a Proof of delivery has arrived, so there is nothing to release",
        "NotRated" => "it is not rated",
        "OpenException" => "an Exception on its Shipment is still open",
        _ => $"of {reason}",
    };

    private static string? ReleaseBlockerAdvice(string reason) => reason switch
    {
        "NotRated" => "Releasing would not make it billable while it is not rated: open its rating exception first.",
        "OpenException" => "The open Exception is the Planner's to resolve.",
        _ => null,
    };

    private static bool Flag(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

    private static string RatingFailureAnswer(string number, ScriptedToolResult order)
    {
        var rating = Object(Parse(order.Content), "rating");
        if (rating is null || Text(rating.Value, "result") is not { } result)
        {
            return IntegrationOperationsScript.Json(
                $"The result shows nothing about the rating of Transport Order {number}, so a rating failure cannot be explained from it.",
                [("The Transport Order came back without a rating result.", order.Id)],
                "Ask a colleague to look at the Transport Order in the TMS.",
                "low",
                "needs-a-person");
        }

        if (result != "RatingFailed")
        {
            return IntegrationOperationsScript.Json(
                $"Rating has not failed for Transport Order {number}; its rating result is {Words(result)}.",
                [($"The rating result is {result}.", order.Id)],
                "Nothing needs fixing here. Reload the rating exceptions list: the order should have left it.",
                "high",
                "needs-a-person");
        }

        var code = Text(rating.Value, "failureCode") ?? "Unknown";
        var message = Text(rating.Value, "failureMessage") ?? "no message";
        var (cause, fix) = code switch
        {
            "NoRateCard" => ("The Shipper has no Rate Card, so there is nothing to price the order from.", "the Shipper's Rate Card is created"),
            "NoValidRateCardVersion" => ("The Shipper's Rate Card has no version that is valid on the collection date.", "a Rate Card version valid on the collection date is added"),
            "NoMatchingLane" => ("The Shipper's Rate Card has no Lane for this origin and destination.", "a Lane for this origin and destination is added to the Rate Card"),
            "WeightAboveHighestBreak" => ("The order's total weight is above the highest Weight Break on its Lane.", "the Lane gets a Weight Break that covers this weight"),
            _ => ($"Rating failed: {message}", "whatever that message says is corrected"),
        };
        return IntegrationOperationsScript.Json(
            $"Rating failed for Transport Order {number}. {cause}",
            [($"Rating failed with {code}: {message}", order.Id)],
            $"Customer service does not edit the Rate Card: ask whoever owns the Rate Card to fix it, so that {fix}, then re-rate the order with the Re-rate action on the rating exceptions list.",
            "high",
            "needs-a-person");
    }

    private static string NotYetBillableAnswer(string number, ScriptedToolResult order)
    {
        var json = Parse(order.Content);
        if (Object(json, "billing") is not { } billing || Text(billing, "state") is not { } state)
        {
            return IntegrationOperationsScript.Json(
                $"The result shows nothing about the billing of Transport Order {number}, so what holds it back cannot be told from it.",
                [("The Transport Order came back without a billing state.", order.Id)],
                "Ask a colleague to look at the Transport Order in the TMS.",
                "low",
                "needs-a-person");
        }

        var reasons = billing.TryGetProperty("blockingReasons", out var array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray().Where(reason => reason.ValueKind == JsonValueKind.String).Select(reason => reason.GetString()!).ToList()
            : [];
        if (state != "NotYetBillable" || reasons.Count == 0)
        {
            return IntegrationOperationsScript.Json(
                $"Nothing holds Transport Order {number} back: its billing state is {Words(state)}.",
                [($"The billing state is {state} and no blocking reason is listed.", order.Id)],
                "Nothing needs doing for this Transport Order.",
                "high",
                "needs-a-person");
        }

        var held = reasons.Select(reason => reason switch
        {
            "NotRated" => "it is not rated",
            "NoProofOfDelivery" => "it has no Proof of delivery",
            "OpenException" => "an Exception on its Shipment is still open",
            _ => $"of {reason}",
        });
        var yours = reasons.Select(reason => reason switch
        {
            "NotRated" => "open its rating exception and, once whoever owns the Rate Card has fixed it, re-rate it",
            "NoProofOfDelivery" => "ask the Carrier for the Proof of delivery, or release the order without one",
            _ => null,
        }).OfType<string>().ToList();
        var notYours = reasons.Contains("OpenException")
            ? " The open Exception is not yours to act on: the Planner has to resolve it."
            : string.Empty;
        return IntegrationOperationsScript.Json(
            $"Transport Order {number} is not yet billable because {string.Join(" and ", held)}.",
            reasons.Select(reason => ($"Billing is blocked by {reason}.", order.Id)),
            (yours.Count == 0 ? "Customer service can do nothing about this yet." : $"You can {string.Join("; and ", yours)}.") + notYours,
            "high",
            "needs-a-person");
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
