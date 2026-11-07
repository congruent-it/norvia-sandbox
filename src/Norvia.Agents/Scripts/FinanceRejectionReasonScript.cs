using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Norvia.Agents.Runner;

namespace Norvia.Agents.Scripts;

/// <summary>
/// The scripted model of the Carrier invoice rejection reason agent. It reads the Carrier-visible facts of the line with its one tool,
/// <c>get_carrier_visible_line_facts</c>, and writes a short English reason for the Carrier from the named fields of that result only: it
/// reads nothing else the result may carry. It negotiates nothing, and a rejected line is resent in a new invoice with a new number (E2).
/// The draft goes in the Suggestion's cause; nothing is sent.
/// </summary>
public static class FinanceRejectionReasonScript
{
    public const string GetCarrierVisibleLineFacts = "get_carrier_visible_line_facts";

    private const string Sign = "\n\nKind regards,\nNorvia Finance";

    public static IChatClient Model() => new ScriptedChatClient(Next);

    private static IReadOnlyList<AIContent> Next(ScriptContext context)
    {
        if (FinanceQuestions.ParseDraftRejectionReason(context.Question) is not { } line)
        {
            return [new TextContent("{}")];
        }

        return context.Results.Count == 0
            ? [new FunctionCallContent(
                $"call-{context.Turn}",
                GetCarrierVisibleLineFacts,
                new Dictionary<string, object?> { ["carrierId"] = line.CarrierId, ["invoiceNumber"] = line.InvoiceNumber, ["carrierBookingReference"] = line.CarrierBookingReference })]
            : [new TextContent(Answer(context.Results[0]))];
    }

    private static string Answer(ScriptedToolResult result)
    {
        JsonElement facts;
        try
        {
            using var document = JsonDocument.Parse(result.Content);
            facts = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return Unreadable(result);
        }

        if (facts.ValueKind != JsonValueKind.Object
            || IntegrationOperationsScript.Text(facts, "result") is not { } outcome
            || IntegrationOperationsScript.Text(facts, "invoiceNumber") is not { } invoice
            || IntegrationOperationsScript.Text(facts, "carrierBookingReference") is not { } reference)
        {
            return Unreadable(result);
        }

        var line = $"line {reference} of your invoice {invoice}";
        var resend = "Please send a new invoice, with a new invoice number";
        return outcome switch
        {
            "UnknownBooking" => Unknown(facts, line, reference, resend, result),
            "Duplicate" => Duplicate(facts, line, resend, result),
            "PriceMismatch" => PriceMismatch(facts, line, resend, result),
            _ => Unreadable(result),
        };
    }

    private static string Unknown(JsonElement facts, string line, string reference, string resend, ScriptedToolResult result)
    {
        var likely = IntegrationOperationsScript.Text(facts, "likelyCorrectReference");
        var claims = new List<(string, string)> { ($"No booking is known under the reference {reference}.", result.Id) };
        var text = $"We have rejected {line}: we have no booking under the reference {reference}.";
        if (likely is not null)
        {
            text += $" The reference may be {likely}.";
            claims.Add(($"{likely} may be the correct reference.", result.Id));
        }

        text += $" {resend}, for the line with the correct reference.";
        return Draft(text, claims, likely is null ? "medium" : "high");
    }

    private static string Duplicate(JsonElement facts, string line, string resend, ScriptedToolResult result)
    {
        if (IntegrationOperationsScript.Text(facts, "earlierInvoiceNumber") is not { } earlier)
        {
            return Unreadable(result);
        }

        return Draft(
            $"We have rejected {line}: it was already invoiced on your earlier invoice {earlier}. Please do not invoice it again; {resend[0].ToString().ToLowerInvariant() + resend[1..]} only for lines that have not been invoiced yet.",
            [($"The line was already invoiced on the Carrier's earlier invoice {earlier}.", result.Id)],
            "high");
    }

    private static string PriceMismatch(JsonElement facts, string line, string resend, ScriptedToolResult result)
    {
        if (Amount(facts, "invoicedAmount") is not { } invoiced || Amount(facts, "agreedCarrierCost") is not { } agreed)
        {
            return Unreadable(result);
        }

        return Draft(
            $"We have rejected {line}: it invoices {Euro(invoiced)}, but the amount agreed when you confirmed the booking is {Euro(agreed)}. {resend}, at the agreed amount.",
            [
                ($"The line invoices {Euro(invoiced)}.", result.Id),
                ($"The amount agreed when the Carrier confirmed the booking is {Euro(agreed)}.", result.Id),
            ],
            "high");
    }

    private static string Draft(string body, IEnumerable<(string Claim, string Citation)> claims, string confidence) =>
        IntegrationOperationsScript.Json(
            $"Hello,\n\n{body}{Sign}",
            claims,
            "Edit the reason, then reject the line yourself: nothing is sent to the Carrier until you confirm.",
            confidence,
            "needs-a-person");

    private static string Unreadable(ScriptedToolResult result) =>
        IntegrationOperationsScript.Json(
            "No reason could be drafted: the facts of the line came back without a result that can be read. Write the reason yourself.",
            [("The line's facts came back without a result that can be read.", result.Id)],
            "Write the reason yourself, then reject the line.",
            "low",
            "needs-a-person");

    private static decimal? Amount(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var amount) ? amount : null;

    private static string Euro(decimal amount) => "€" + amount.ToString("0.00", CultureInfo.InvariantCulture);
}
