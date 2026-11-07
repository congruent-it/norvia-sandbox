using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Norvia.Agents.Runner;

namespace Norvia.Agents.Scripts;

/// <summary>
/// The scripted model of the Carrier invoice matching agent. It reads the line the question names, then explains in plain language why
/// it did not match, citing the result: a Price mismatch with the difference and the tolerance, a Duplicate with the earlier line that
/// Invoiced the Carrier cost, an Unknown booking with its likely candidates, never as a certainty. It says whether the line may still
/// resolve, and it never states a fact the tool did not return.
/// </summary>
public static class FinanceMatchingScript
{
    public const string GetInvoiceLine = "get_invoice_line";

    public static IChatClient Model() => new ScriptedChatClient(Next);

    private static IReadOnlyList<AIContent> Next(ScriptContext context)
    {
        if (FinanceQuestions.ParseExplainInvoiceLine(context.Question) is not { } line)
        {
            return [new TextContent("{}")];
        }

        return context.Results.Count == 0
            ? [new FunctionCallContent(
                $"call-{context.Turn}",
                GetInvoiceLine,
                new Dictionary<string, object?> { ["carrierId"] = line.CarrierId, ["invoiceNumber"] = line.InvoiceNumber, ["carrierBookingReference"] = line.CarrierBookingReference })]
            : [new TextContent(Answer(context.Results[0]))];
    }

    private static string Answer(ScriptedToolResult result)
    {
        JsonElement view;
        try
        {
            using var document = JsonDocument.Parse(result.Content);
            view = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return Unreadable(result);
        }

        if (view.ValueKind != JsonValueKind.Object || IntegrationOperationsScript.Text(view, "result") is not { } outcome)
        {
            return Unreadable(result);
        }

        return outcome switch
        {
            "PriceMismatch" => PriceMismatch(view, result),
            "UnknownBooking" => UnknownBooking(view, result),
            "Duplicate" => Duplicate(view, result),
            _ => IntegrationOperationsScript.Json(
                $"The line's result is {outcome}, so there is no mismatch to explain.",
                [($"The line's result is {outcome}.", result.Id)],
                "Nothing to decide on this line.",
                "high",
                "needs-a-person"),
        };
    }

    private static string PriceMismatch(JsonElement view, ScriptedToolResult result)
    {
        var invoiced = Amount(view, "invoicedAmount");
        var tolerance = Amount(view, "tolerance");
        var difference = Amount(view, "difference");
        if (view.TryGetProperty("carrierCost", out var cost) && cost.ValueKind == JsonValueKind.Object && Amount(cost, "total") is { } agreed && invoiced is { } claimed)
        {
            var source = IntegrationOperationsScript.Text(cost, "source") ?? "an unknown source";
            var by = difference is { } gap ? $"{Euro(Math.Abs(gap))} {(gap > 0 ? "more" : "less")} than" : "a different amount from";
            var advice = source == "Spot"
                ? "Ask the Planner what was agreed for this Spot price before you decide. Then accept the line at the agreed amount with a remark, or reject it and ask the Carrier for a corrected invoice."
                : "Accept the line at the agreed amount with a remark, or reject it and ask the Carrier for a corrected invoice.";
            return IntegrationOperationsScript.Json(
                $"The Carrier claims {Euro(claimed)}, {by} the agreed Carrier cost of {Euro(agreed)} ({source}). The tolerance is {Euro(tolerance ?? 0m)}, so the line did not match. It stays To decide until Finance decides it; it does not resolve by itself.",
                [
                    ($"The Carrier claims {Euro(claimed)} for the line.", result.Id),
                    ($"The agreed Carrier cost is {Euro(agreed)}, from {source}.", result.Id),
                    ($"The matching tolerance is {Euro(tolerance ?? 0m)}.", result.Id),
                ],
                advice,
                "high",
                "needs-a-person");
        }

        return Unreadable(result);
    }

    private static string UnknownBooking(JsonElement view, ScriptedToolResult result)
    {
        var reference = IntegrationOperationsScript.Text(view, "carrierBookingReference") ?? "the line's reference";
        var candidates = view.TryGetProperty("candidates", out var list) && list.ValueKind == JsonValueKind.Array ? list.EnumerateArray().ToList() : [];
        var resolves = view.TryGetProperty("resolvesAutomatically", out var flag) && flag.ValueKind == JsonValueKind.True;
        var resolveText = resolves
            ? "The line is matched again by itself when a Carrier cost for this reference arrives."
            : "The line does not resolve by itself.";

        if (candidates.Count == 0)
        {
            return IntegrationOperationsScript.Json(
                $"No Carrier cost has arrived under the reference {reference}, and no Open Carrier cost of this Carrier looks like it. {resolveText}",
                [($"The line is an Unknown booking and the ERP found no likely candidate for {reference}.", result.Id)],
                resolves
                    ? "Wait for the Carrier cost to arrive, or reject the line and ask the Carrier for the right reference."
                    : "Reject the line and ask the Carrier for the right reference.",
                "medium",
                "needs-a-person");
        }

        var best = candidates[0];
        var bestNumber = IntegrationOperationsScript.Text(best, "carrierBookingNumber") ?? "unknown";
        var bestReference = IntegrationOperationsScript.Text(best, "carrierBookingReference") ?? "unknown";
        var bestTotal = Amount(best, "total");
        var others = candidates.Count > 1 ? $" {candidates.Count - 1} other likely candidate{(candidates.Count > 2 ? "s are" : " is")} listed." : string.Empty;
        return IntegrationOperationsScript.Json(
            $"No Carrier cost has arrived under the reference {reference}. The Open Carrier cost {bestNumber} (reference {bestReference}{(bestTotal is { } total ? $", {Euro(total)}" : string.Empty)}) is a likely candidate, but that is not certain: the ERP ranks on how alike the references are, the amount and the delivery date.{others} {resolveText}",
            [
                ($"The line is an Unknown booking: no Carrier cost with the reference {reference}.", result.Id),
                ($"The best likely candidate is Carrier cost {bestNumber}, reference {bestReference}.", result.Id),
            ],
            $"Check with the Carrier whether the line is about Carrier cost {bestNumber} before you accept it. If it is not, wait for the Carrier cost to arrive or reject the line.",
            "medium",
            "needs-a-person");
    }

    private static string Duplicate(JsonElement view, ScriptedToolResult result)
    {
        var reference = IntegrationOperationsScript.Text(view, "carrierBookingReference") ?? "the line's reference";
        if (view.TryGetProperty("invoicedBy", out var earlier) && earlier.ValueKind == JsonValueKind.Object)
        {
            var invoice = IntegrationOperationsScript.Text(earlier, "invoiceNumber") ?? "an earlier invoice";
            var line = IntegrationOperationsScript.Text(earlier, "carrierBookingReference") ?? reference;
            var how = IntegrationOperationsScript.Text(earlier, "status")?.ToLowerInvariant() ?? "decided";
            var date = IntegrationOperationsScript.Text(earlier, "invoiceDate");
            var amount = Amount(earlier, "amount");
            return IntegrationOperationsScript.Json(
                $"The Carrier cost for {reference} is already Invoiced: line {line} of Carrier invoice {invoice}{(date is null ? string.Empty : $" ({date})")} was {how}{(amount is { } paid ? $" at {Euro(paid)}" : string.Empty)}. This line claims it a second time. It does not resolve by itself.",
                [
                    ($"The line is a Duplicate of line {line} of Carrier invoice {invoice}.", result.Id),
                    ($"The earlier line was {how}.", result.Id),
                ],
                $"Reject the line with the reason that it duplicates Carrier invoice {invoice}, unless the Carrier meant a different booking.",
                "high",
                "needs-a-person");
        }

        return IntegrationOperationsScript.Json(
            $"The Carrier cost for {reference} is already Invoiced, but the result does not show by which earlier line.",
            [($"The line is a Duplicate of an earlier line that the result does not name.", result.Id)],
            "Look for the earlier Carrier invoice before you decide.",
            "low",
            "needs-a-person");
    }

    private static string Unreadable(ScriptedToolResult result) =>
        IntegrationOperationsScript.Json(
            "The result does not show why the line did not match.",
            [("The line came back without a result that can be read.", result.Id)],
            "Ask a colleague to look at the Carrier invoice's Message trail.",
            "low",
            "needs-a-person");

    private static decimal? Amount(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var amount) ? amount : null;

    private static string Euro(decimal amount) => "€" + amount.ToString("0.00", CultureInfo.InvariantCulture);
}
