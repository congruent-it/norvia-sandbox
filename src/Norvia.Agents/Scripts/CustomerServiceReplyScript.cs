using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Norvia.Agents.Runner;

namespace Norvia.Agents.Scripts;

/// <summary>
/// The scripted model of the Customer service reply agent. It reads the Shipper view of the Transport Order with its one tool,
/// <c>get_shipper_view</c>, and writes a short English reply to the Shipper's question from that result only. It states no price and
/// no Carrier, and says "we have no update" for what the result does not show. The draft goes in the Suggestion's cause; nothing is sent.
/// </summary>
public static class CustomerServiceReplyScript
{
    public const string GetShipperView = "get_shipper_view";

    public static IChatClient Model() => new ScriptedChatClient(Next);

    private static IReadOnlyList<AIContent> Next(ScriptContext context)
    {
        if (CustomerServiceQuestions.ParseDraftReply(context.Question) is not { } asked)
        {
            return [new TextContent("{}")];
        }

        return context.Results.Count == 0
            ? [new FunctionCallContent($"call-{context.Turn}", GetShipperView, new Dictionary<string, object?> { ["transportOrderNumber"] = asked.TransportOrderNumber })]
            : [new TextContent(Answer(asked.TransportOrderNumber, asked.Question, context.Results[0]))];
    }

    private static string Answer(string number, ShipperQuestion question, ScriptedToolResult result)
    {
        JsonElement view;
        try
        {
            using var document = JsonDocument.Parse(result.Content);
            view = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            view = default;
        }

        if (view.ValueKind != JsonValueKind.Object || IntegrationOperationsScript.Text(view, "trackingStatus") is not { } status)
        {
            return Json(
                $"Hello,\n\nWe have no update on Transport Order {number} at the moment.\n\nKind regards,\nNorvia Customer service",
                [("The Shipper view came back without a Tracking status.", result.Id)],
                "low");
        }

        var (body, claims) = question switch
        {
            ShipperQuestion.HasItBeenDelivered => Delivered(view, status, result.Id),
            ShipperQuestion.IsThereProofOfDelivery => ProofOfDelivery(view, result.Id),
            _ => Where(view, status, result.Id),
        };
        return Json($"Hello,\n\n{body}\n\nKind regards,\nNorvia Customer service", claims, "high");
    }

    private static (string Body, List<(string, string)> Claims) Where(JsonElement view, string status, string id)
    {
        var claims = new List<(string, string)> { ($"The Tracking status is {status}.", id) };
        var latest = LatestProgress(view);
        var text = $"Transport Order {Number(view)} is {Plain(status)}.";
        if (latest is { } progress)
        {
            text += $" Its latest update is {Plain(progress.Type)} in {progress.Place} on {progress.Time}.";
            claims.Add(($"The latest progress is {progress.Type} in {progress.Place} on {progress.Time}.", id));
        }
        else
        {
            text += " We have no update on its movements yet.";
        }

        if (status != "Delivered" && Time(view, "eta") is { } eta)
        {
            text += $" We expect it on {eta}.";
            claims.Add(($"The ETA is {eta}.", id));
        }

        return (text, claims);
    }

    private static (string Body, List<(string, string)> Claims) Delivered(JsonElement view, string status, string id)
    {
        if (status == "Delivered")
        {
            var when = LatestProgress(view, "Delivered")?.Time ?? Time(view, "lastUpdate");
            return (
                when is null ? $"Yes, Transport Order {Number(view)} has been delivered." : $"Yes, Transport Order {Number(view)} was delivered on {when}.",
                [($"The Tracking status is Delivered{(when is null ? string.Empty : $", on {when}")}.", id)]);
        }

        var text = $"Transport Order {Number(view)} has not been delivered yet. It is {Plain(status)}.";
        var claims = new List<(string, string)> { ($"The Tracking status is {status}, not Delivered.", id) };
        if (Time(view, "eta") is { } eta)
        {
            text += $" We expect it on {eta}.";
            claims.Add(($"The ETA is {eta}.", id));
        }

        return (text, claims);
    }

    private static (string Body, List<(string, string)> Claims) ProofOfDelivery(JsonElement view, string id)
    {
        if (view.TryGetProperty("proofOfDelivery", out var proof) && proof.ValueKind == JsonValueKind.Object
            && proof.TryGetProperty("available", out var available) && available.ValueKind == JsonValueKind.True)
        {
            var when = Time(proof, "deliveryTime");
            var condition = IntegrationOperationsScript.Text(proof, "condition") == "Reservations" ? "with reservations" : "clean";
            return (
                $"Yes, a Proof of delivery is available for Transport Order {Number(view)}. The goods were delivered{(when is null ? string.Empty : $" on {when}")} and received {condition}.",
                [($"A Proof of delivery is available, received {condition}.", id)]);
        }

        return ($"There is no Proof of delivery for Transport Order {Number(view)} yet. We have no update on when it will be available.", [("No Proof of delivery is available.", id)]);
    }

    private static string Number(JsonElement view) => IntegrationOperationsScript.Text(view, "transportOrderNumber") ?? "your order";

    private static string Plain(string value) => value switch
    {
        "Received" => "received and not yet scheduled",
        "Scheduled" => "scheduled for collection",
        "Collected" => "collected",
        "InTransit" => "in transit",
        "Delivered" => "delivered",
        "Departed" => "departed",
        "Arrived" => "arrived",
        "OutForDelivery" => "out for delivery",
        _ => value,
    };

    private static (string Type, string Place, string Time)? LatestProgress(JsonElement view, string? type = null)
    {
        if (!view.TryGetProperty("progress", out var progress) || progress.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        (string Type, string Place, string Time)? latest = null;
        DateTimeOffset latestAt = default;
        foreach (var entry in progress.EnumerateArray().Where(entry => entry.ValueKind == JsonValueKind.Object))
        {
            var entryType = IntegrationOperationsScript.Text(entry, "type");
            if (entryType is null || (type is not null && entryType != type)
                || !entry.TryGetProperty("eventTime", out var time) || !time.TryGetDateTimeOffset(out var at) || (latest is not null && at < latestAt))
            {
                continue;
            }

            var place = entry.TryGetProperty("place", out var p) && p.ValueKind == JsonValueKind.Object ? IntegrationOperationsScript.Text(p, "city") : null;
            latest = (entryType, place ?? "an unnamed place", Format(at));
            latestAt = at;
        }

        return latest;
    }

    private static string? Time(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String && value.TryGetDateTimeOffset(out var at) ? Format(at) : null;

    private static string Format(DateTimeOffset at) => at.ToUniversalTime().ToString("d MMMM yyyy 'at' HH:mm 'UTC'", CultureInfo.InvariantCulture);

    private static string Json(string draft, IEnumerable<(string Claim, string Citation)> evidence, string confidence) =>
        JsonSerializer.Serialize(new
        {
            cause = draft,
            evidence = evidence.Select(item => new { claim = item.Claim, citation = item.Citation }),
            action = "needs-a-person",
            suggestedAction = "Edit the draft, then send it to the Shipper yourself: the TMS sends nothing.",
            confidence,
        });
}
