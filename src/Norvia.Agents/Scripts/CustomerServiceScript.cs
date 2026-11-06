using System.Text.Json;
using Microsoft.Extensions.AI;
using Norvia.Agents.Runner;

namespace Norvia.Agents.Scripts;

/// <summary>
/// The scripted model of the Customer service agent. It reads the Rejected booking the question names, then explains each rejection
/// reason in plain language and says what the Shipper should change, citing the result. Reasons it knows (a bad weight, a delivery
/// window before the collection, a missing address) get their own wording; any other is told in the reason's own message. It never
/// states a fact the tool did not return, and a result with no reasons is told so with low confidence.
/// </summary>
public static class CustomerServiceScript
{
    public const string GetRejectedBooking = "get_rejected_booking";

    public static IChatClient Model() => new ScriptedChatClient(Next);

    private static IReadOnlyList<AIContent> Next(ScriptContext context)
    {
        if (CustomerServiceQuestions.ParseSummariseTransportOrder(context.Question) is { } number)
        {
            return CustomerServiceOrderScript.Next(context, number);
        }

        if (CustomerServiceQuestions.ParseExplainRejectedBooking(context.Question) is not { } id)
        {
            return [new TextContent("{}")];
        }

        return context.Results.Count == 0
            ? [new FunctionCallContent($"call-{context.Turn}", GetRejectedBooking, new Dictionary<string, object?> { ["id"] = id.ToString("D") })]
            : [new TextContent(Answer(context.Results[0]))];
    }

    private static string Answer(ScriptedToolResult result)
    {
        var reasons = ReasonsOf(result.Content);
        if (reasons.Count == 0)
        {
            return IntegrationOperationsScript.Json(
                "The result shows no rejection reason, so the booking's rejection cannot be explained from it.",
                [("The Rejected booking came back with no rejection reasons.", result.Id)],
                "Ask a colleague to look at the booking's Message trail, and do not tell the Shipper a reason yet.",
                "low",
                "needs-a-person");
        }

        var cause = string.Join(" ", reasons.Select(reason => Plain(reason).Cause));
        var advice = string.Join(" ", reasons.Select(reason => Plain(reason).Advice).Distinct());
        var evidence = reasons.Select(reason => ($"The booking was rejected with {reason.Code}: {reason.Message}", result.Id));
        return IntegrationOperationsScript.Json(
            reasons.Count == 1 ? cause : $"The booking broke {reasons.Count} rules. {cause}",
            evidence,
            $"Tell the Shipper: {advice} Then they can send the booking again.",
            "high",
            "needs-a-person");
    }

    private static (string Cause, string Advice) Plain(Reason reason) => reason.Code switch
    {
        "WeightInvalid" => ("A goods line has a weight that is zero, negative or missing.", "Give every goods line a weight in kilograms above zero."),
        "TotalWeightOverLimit" => ("The goods weigh more in total than one Transport Order may carry.", "Split the goods over more than one booking."),
        "QuantityInvalid" => ("A goods line has a quantity that is zero, negative or missing.", "Give every goods line a whole quantity of at least one."),
        "PackageTypeInvalid" => ("A goods line has a package type Norvia does not know.", "Use one of the package types Norvia accepts."),
        "DimensionsInvalid" => ("A goods line has dimensions that are zero or negative.", "Give each dimension in centimetres above zero, or leave them out."),
        "GoodsLinesMissing" => ("The booking has no goods.", "Add at least one goods line."),
        "CollectionAddressMissing" or "CollectionAddressIncomplete" => ("The collection address is missing or incomplete.", "Give the full collection address: street, postcode, city and country."),
        "DeliveryAddressMissing" or "DeliveryAddressIncomplete" => ("The delivery address is missing or incomplete.", "Give the full delivery address: street, postcode, city and country."),
        "ConsigneeNameMissing" => ("The booking does not name who the goods are delivered to.", "Name the Consignee."),
        "CollectionWindowMissing" => ("The booking has no collection window.", "Give the earliest and latest time for collection."),
        "CollectionWindowInPast" => ("The collection window has already passed.", "Choose a collection window in the future."),
        "CollectionWindowReversed" => ("The collection window ends before it starts.", "Swap the earliest and latest collection times."),
        "DeliveryWindowIncomplete" => ("The delivery window has a start or an end but not both.", "Give both the earliest and the latest delivery time, or neither."),
        "DeliveryWindowReversed" => ("The delivery window ends before it starts.", "Swap the earliest and latest delivery times."),
        "DeliveryWindowBeforeCollection" => ("The delivery window starts before the goods can be collected.", "Move the delivery window to after the collection window."),
        "ShipperReferenceMissing" => ("The booking has no Shipper reference.", "Give the booking a reference of the Shipper's own."),
        "ShipperReferenceTooLong" => ("The Shipper reference is longer than Norvia accepts.", "Shorten the Shipper reference."),
        "ShipperReferenceReused" => ("The Shipper reference was already used for a different booking.", "Use a new Shipper reference for a different booking."),
        "UnknownShipper" or "InactiveShipper" => ("Norvia does not have this Shipper as an active customer.", "Check the Shipper's account with Norvia before they book again."),
        _ => (reason.Message, "Correct what the message says."),
    };

    private static List<Reason> ReasonsOf(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("rejectionReasons", out var reasons) || reasons.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return
            [
                .. reasons.EnumerateArray()
                    .Where(reason => reason.ValueKind == JsonValueKind.Object)
                    .Select(reason => new Reason(IntegrationOperationsScript.Text(reason, "code") ?? "Unknown", IntegrationOperationsScript.Text(reason, "message") ?? "The booking broke a rule.")),
            ];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private sealed record Reason(string Code, string Message);
}
