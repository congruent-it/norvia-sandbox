using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Norvia.Agents.Runner;

namespace Norvia.Agents.Scripts;

/// <summary>
/// The scripted model of the Planner notification agent. It reads the template of the Notify Shipper message with
/// <c>get_shipper_notification_template</c> and what the Shipper sees of the Transport Order with <c>get_shipper_view</c>, and writes the
/// message from those two results only: the template's words, then the current Tracking status, the latest progress and the ETA when the
/// view has them. It names no Carrier, remark or Exception, states no price and promises nothing: where the view does not say, it keeps
/// the template's words and says "we have no update". The draft goes in the Suggestion's cause; nothing is sent.
/// </summary>
public static class PlannerNotificationScript
{
    public const string GetTemplate = "get_shipper_notification_template";
    public const string GetShipperView = "get_shipper_view";

    public static IChatClient Model() => new ScriptedChatClient(Next);

    private static IReadOnlyList<AIContent> Next(ScriptContext context)
    {
        if (PlannerQuestions.ParseDraftShipperNotification(context.Question) is not { } asked)
        {
            return [new TextContent("{}")];
        }

        return context.Results.Count == 0
            ?
            [
                new FunctionCallContent($"call-{context.Turn}-template", GetTemplate, new Dictionary<string, object?> { ["exceptionNumber"] = asked.ExceptionNumber }),
                new FunctionCallContent($"call-{context.Turn}-view", GetShipperView, new Dictionary<string, object?> { ["transportOrderNumber"] = asked.TransportOrderNumber }),
            ]
            : [new TextContent(Answer(context.Results))];
    }

    private static string Answer(IReadOnlyList<ScriptedToolResult> results)
    {
        var templateResult = results.FirstOrDefault(result => result.Tool == GetTemplate);
        var viewResult = results.FirstOrDefault(result => result.Tool == GetShipperView);
        if (templateResult is null || viewResult is null
            || Parse(templateResult.Content) is not { ValueKind: JsonValueKind.Object } template || IntegrationOperationsScript.Text(template, "message") is not { Length: > 0 } message
            || Parse(viewResult.Content) is not { ValueKind: JsonValueKind.Object } view || IntegrationOperationsScript.Text(view, "trackingStatus") is not { } status)
        {
            return Json(
                "The template or the Shipper view could not be read, so no draft can be written.",
                [("A result came back unreadable.", (templateResult ?? viewResult)?.Id ?? "r1")],
                "low");
        }

        var claims = new List<(string, string)> { ("The template's words for this kind of problem.", templateResult.Id), ($"The Tracking status is {status}.", viewResult.Id) };
        var body = message + $" Your order is {Plain(status)}.";
        if (LatestProgress(view) is { } progress)
        {
            body += $" Its latest update is {Plain(progress.Type)} in {progress.Place} on {progress.Time}.";
            claims.Add(($"The latest progress is {progress.Type} in {progress.Place} on {progress.Time}.", viewResult.Id));
        }

        if (status != "Delivered")
        {
            if (Time(view, "eta") is { } eta)
            {
                body += $" The current expected delivery is {eta}.";
                claims.Add(($"The ETA is {eta}.", viewResult.Id));
            }
            else
            {
                body += " We have no update on a new delivery time yet.";
                claims.Add(("The view has no ETA.", viewResult.Id));
            }
        }

        return Json($"Hello,\n\n{body}\n\nKind regards,\nNorvia Planning", claims, "medium");
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

    private static (string Type, string Place, string Time)? LatestProgress(JsonElement view)
    {
        if (!view.TryGetProperty("progress", out var progress) || progress.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        (string Type, string Place, string Time)? latest = null;
        DateTimeOffset latestAt = default;
        foreach (var entry in progress.EnumerateArray().Where(entry => entry.ValueKind == JsonValueKind.Object))
        {
            if (IntegrationOperationsScript.Text(entry, "type") is not { } type
                || !entry.TryGetProperty("eventTime", out var time) || !time.TryGetDateTimeOffset(out var at) || (latest is not null && at < latestAt))
            {
                continue;
            }

            var place = entry.TryGetProperty("place", out var p) && p.ValueKind == JsonValueKind.Object ? IntegrationOperationsScript.Text(p, "city") : null;
            latest = (type, place ?? "an unnamed place", Format(at));
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
            suggestedAction = "Edit the draft, then send it through Notify Shipper yourself.",
            confidence,
        });
}
