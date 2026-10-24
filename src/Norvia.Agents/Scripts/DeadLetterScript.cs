using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Norvia.Agents.Scripts;

/// <summary>
/// The scripted answer to "explain this dead-lettered message": it peeks the queue, finds the message by its sequence number and
/// tells its dead-letter reason. For the P5 message, a Carrier's <c>DLV</c> where the map knows only <c>POD</c>, it tells the demo's answer.
/// </summary>
internal static class DeadLetterScript
{
    public static IReadOnlyList<AIContent> Next(ScriptContext context, string entity, long sequenceNumber)
    {
        if (context.Results.Count == 0)
        {
            return [new FunctionCallContent($"call-{context.Turn}", IntegrationOperationsScript.PeekDeadLetters, new Dictionary<string, object?> { ["entity"] = entity })];
        }

        var peeked = context.Results[0];
        return [new TextContent(Answer(peeked.Id, entity, sequenceNumber, Find(peeked.Content, sequenceNumber)))];
    }

    private static string Answer(string peekedId, string entity, long sequenceNumber, Message? message)
    {
        if (message is null)
        {
            return IntegrationOperationsScript.Json(
                $"Message {sequenceNumber} isn't among the oldest dead-lettered messages of {entity} any more, so there is nothing to read about it.",
                [("The peek of the queue shows no message with that sequence number.", peekedId)],
                "Someone may have resubmitted or discarded it meanwhile: open its Message trail to see.",
                "low");
        }

        var reason = message.Reason ?? "no reason given";
        var evidence = new List<(string Claim, string Citation)>
        {
            ($"Message {sequenceNumber} was dead-lettered with the reason '{reason}'{(message.Description is null ? string.Empty : $": {message.Description}")}, after {message.DeliveryCount} deliveries.", peekedId),
        };
        return message.Reason == IntegrationOperationsScript.UnmappableTrackingMessage
            ? IntegrationOperationsScript.Json(
                "The Carrier-tracking flow could not map the status code in the Carrier's tracking message, so it dead-lettered the message. The Carrier was still answered 202, so the Shipment stays In transit and nobody was told.",
                evidence,
                "Ask the Carrier to send the Delivered event again with a status code the map knows (POD, not DLV). Then resubmit this message, or discard it once the Carrier's resend has arrived.",
                "high")
            : IntegrationOperationsScript.Json(
                $"The message was dead-lettered with the reason '{reason}'.",
                evidence,
                "Fix what the reason names before you resubmit the message, or discard it when it is no longer wanted.",
                message.Reason is null ? "low" : "medium");
    }

    private static Message? Find(string content, long sequenceNumber)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var message in messages.EnumerateArray())
            {
                if (message.TryGetProperty("sequenceNumber", out var number) && number.TryGetInt64(out var found) && found == sequenceNumber)
                {
                    return new Message(
                        IntegrationOperationsScript.Text(message, "reason"),
                        IntegrationOperationsScript.Text(message, "description"),
                        message.TryGetProperty("deliveryCount", out var deliveries) && deliveries.TryGetInt32(out var count) ? count : 0);
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private sealed record Message(string? Reason, string? Description, int DeliveryCount);
}
