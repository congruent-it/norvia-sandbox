using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Norvia.Agents.Runner;

namespace Norvia.Agents.Scripts;

/// <summary>
/// The scripted model of the Planner agent's draft Exception note. It reads the Exception's timeline with its one tool,
/// <c>get_exception_timeline</c>, and writes a short note of what has happened and what is outstanding from that result only. It names no
/// outcome, states no cause the timeline does not show, and never resolves anything: the draft goes in the Suggestion's cause and the
/// Planner edits and saves it.
/// </summary>
public static class PlannerExceptionNoteScript
{
    public const string GetExceptionTimeline = "get_exception_timeline";

    public static IChatClient Model() => new ScriptedChatClient(Next);

    private static IReadOnlyList<AIContent> Next(ScriptContext context)
    {
        if (PlannerQuestions.ParseDraftExceptionNote(context.Question) is not { } number)
        {
            return [new TextContent("{}")];
        }

        return context.Results.Count == 0
            ? [new FunctionCallContent($"call-{context.Turn}", GetExceptionTimeline, new Dictionary<string, object?> { ["exceptionNumber"] = number })]
            : [new TextContent(Answer(number, context.Results[0]))];
    }

    private static string Answer(string number, ScriptedToolResult result)
    {
        const string SuggestedAction = "Edit the draft, then save it as a note yourself.";
        var timeline = Parse(result.Content);
        if (timeline is not { ValueKind: JsonValueKind.Object } view || Text(view, "exceptionNumber") is null)
        {
            return IntegrationOperationsScript.Json(
                $"The timeline of Exception {number} could not be read, so no note can be drafted.",
                [("The timeline came back unreadable.", result.Id)],
                "Write the note yourself; the assistant has no draft.",
                "low",
                "needs-a-person");
        }

        var exception = Text(view, "exceptionNumber")!;
        if (Text(view, "status") == "Resolved")
        {
            return IntegrationOperationsScript.Json(
                $"Exception {exception} is already resolved, so there is nothing outstanding to draft a note about.",
                [($"Exception {exception} is Resolved.", result.Id)],
                "Write the note yourself if one is still needed.",
                "low",
                "needs-a-person");
        }

        var evidence = new List<(string Claim, string Citation)>();
        var sentences = new List<string>
        {
            $"{Plain(Text(view, "type") ?? "Exception")} on Shipment {Text(view, "shipmentNumber")}, carried by {Text(view, "carriedBy")}, Shipment {Plain(Text(view, "shipmentStatus") ?? string.Empty)}, opened {Time(view, "openedAt")}.",
        };
        evidence.Add(($"{exception} is a {Text(view, "type")} Exception on Shipment {Text(view, "shipmentNumber")}, carried by {Text(view, "carriedBy")}.", result.Id));

        var events = Items(view, "trackingEvents");
        if (events.Count == 0)
        {
            sentences.Add("No Tracking events are recorded for the Shipment.");
            evidence.Add(("The Shipment has no Tracking events.", result.Id));
        }
        else
        {
            var last = events[^1];
            var remark = Text(last, "remark") is { Length: > 0 } text ? $" (remark: \"{text}\")" : string.Empty;
            sentences.Add($"The last Tracking event is {Plain(Text(last, "type") ?? string.Empty)} in {Text(last, "place")} on {Time(last, "eventTime")}, reported by {Text(last, "source") ?? "an unknown source"}{remark}.");
            evidence.Add(($"The last Tracking event is {Text(last, "type")} in {Text(last, "place")} on {Time(last, "eventTime")}.", result.Id));
        }

        var done = Done(view, evidence, result.Id);
        sentences.Add(done.Count == 0 ? "Done so far: nothing is recorded." : "Done so far: " + string.Join("; ", done) + ".");

        var outstanding = new List<string>();
        var notifications = Items(view, "shipperNotifications");
        if (notifications.Count == 0)
        {
            outstanding.Add("The Shipper has not been notified.");
        }
        else if (Text(notifications[^1], "status") == "NotDelivered")
        {
            outstanding.Add("The last Shipper notification did not reach the Shipper.");
        }
        else if (Text(notifications[^1], "status") == "Requested")
        {
            outstanding.Add("The last Shipper notification is not confirmed as sent yet.");
        }

        if (Items(view, "carrierBookingCancellations").Any(cancellation => Flag(cancellation, "carrierNotTold")))
        {
            outstanding.Add("The Carrier was not told of the cancellation.");
        }

        if (Flag(view, "causeCleared"))
        {
            outstanding.Add("The cause no longer shows in the timeline.");
        }

        outstanding.Add("The Exception stays open until the Planner resolves it.");
        sentences.Add("Outstanding: " + string.Join(' ', outstanding));
        evidence.Add(($"{Items(view, "shipperNotifications").Count} Shipper notification(s) are recorded.", result.Id));

        return IntegrationOperationsScript.Json(string.Join(' ', sentences), evidence, SuggestedAction, "medium", "needs-a-person");
    }

    private static List<string> Done(JsonElement view, List<(string Claim, string Citation)> evidence, string id)
    {
        var done = new List<string>();
        foreach (var notification in Items(view, "shipperNotifications"))
        {
            var status = Text(notification, "status");
            done.Add(status == "Sent" ? $"Shipper notification sent {Time(notification, "sentAt")}" : $"Shipper notification {Plain(status ?? "requested")} (requested {Time(notification, "requestedAt")})");
        }

        foreach (var cancellation in Items(view, "carrierBookingCancellations"))
        {
            done.Add($"Carrier booking {Text(cancellation, "carrierBookingNumber")} cancelled {Time(cancellation, "cancelledAt")} (reason: \"{Text(cancellation, "reason")}\")");
        }

        var windows = Items(view, "agreedDeliveryWindows");
        if (windows.Count > 0)
        {
            var window = windows[^1];
            done.Add($"delivery window agreed, {Time(window, "earliest")} to {Time(window, "latest")}");
        }

        var notes = Items(view, "notes");
        if (notes.Count > 0)
        {
            done.Add($"{notes.Count} earlier note{(notes.Count == 1 ? string.Empty : "s")}");
        }

        if (done.Count > 0)
        {
            evidence.Add(($"{done.Count} action(s) or note(s) are already recorded.", id));
        }

        return done;
    }

    private static JsonElement? Parse(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonElement element, string property) => IntegrationOperationsScript.Text(element, property);

    private static bool Flag(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

    private static List<JsonElement> Items(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Array
            ? [.. value.EnumerateArray()]
            : [];

    private static string Time(JsonElement element, string property) =>
        Text(element, property) is { } text && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time.ToString("yyyy-MM-dd HH:mm zzz", CultureInfo.InvariantCulture)
            : "at an unknown time";

    // CamelCase or PascalCase to lower words, such as DeliveryFailed to "delivery failed", but keeping the first letter capital for a sentence start.
    private static string Plain(string name)
    {
        var words = System.Text.RegularExpressions.Regex.Replace(name, "(?<=[a-z])(?=[A-Z])", " ", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1));
        return words.Length == 0 ? words : char.ToUpperInvariant(words[0]) + words[1..].ToLowerInvariant();
    }
}
