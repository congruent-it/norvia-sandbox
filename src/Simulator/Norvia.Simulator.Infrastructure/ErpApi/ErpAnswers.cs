using System.Text.Json;
using System.Text.Json.Nodes;
using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Domain.Log;

namespace Norvia.Simulator.Infrastructure.ErpApi;

/// <summary>
/// Reads the ERP API's answers to Invoice now and to a Credit note (its OpenAPI document): the invoice or Credit note it issued, a refusal with its code, or
/// nothing to invoice. An answer that says none of these is not received.
/// </summary>
internal static class ErpAnswers
{
    public static InvoiceNowAnswer Invoiced(int statusCode, string body)
    {
        var json = Parse(body) as JsonObject;
        if (statusCode == 200 && json is not null && json.ContainsKey("invoice"))
        {
            if (json["invoice"] is not JsonObject invoice)
            {
                return new InvoiceNowAnswer(LogOutcome.NothingToInvoice, null, null, [], null);
            }

            if (Text(invoice, "invoiceNumber") is { } number && invoice["lines"] is JsonArray lines && lines.Count > 0)
            {
                return new InvoiceNowAnswer(
                    LogOutcome.InvoiceIssued, Text(invoice, "correlationId"), number, [.. lines.OfType<JsonObject>().Select(Line)], null, Amount(invoice, "totalWithoutVat"));
            }
        }

        // A refusal names its rule by a stable code. Never its message.
        if (statusCode is >= 400 and < 500 && Text(json, "code") is { } code)
        {
            return new InvoiceNowAnswer(LogOutcome.InvoicingRefused, null, null, [], $"{code} ({statusCode})");
        }

        return new InvoiceNowAnswer(LogOutcome.InvoicingNotReceived, null, null, [], $"The ERP API answered {statusCode} without an answer to Invoice now.");
    }

    public static CreditNoteAnswer CreditNote(int statusCode, string body)
    {
        var json = Parse(body) as JsonObject;
        if (statusCode == 201 && json is not null && Text(json, "creditNoteNumber") is { } number)
        {
            return new CreditNoteAnswer(LogOutcome.CreditNoteIssued, Text(json, "correlationId"), number, null);
        }

        if (statusCode is >= 400 and < 500 && Text(json, "code") is { } code)
        {
            return new CreditNoteAnswer(LogOutcome.InvoicingRefused, Text(json, "correlationId"), null, $"{code} ({statusCode})");
        }

        return new CreditNoteAnswer(LogOutcome.InvoicingNotReceived, null, null, $"The ERP API answered {statusCode} without an answer to the Credit note.");
    }

    public static InvoiceNowAnswer InvoiceNotReceived(string detail) => new(LogOutcome.InvoicingNotReceived, null, null, [], detail);

    public static CreditNoteAnswer CreditNoteNotReceived(string detail) => new(LogOutcome.InvoicingNotReceived, null, null, detail);

    private static InvoicedLine Line(JsonObject line) => new(Text(line, "transportOrderNumber") ?? string.Empty, Text(line, "shipperReference") ?? string.Empty, Amount(line, "total"));

    private static decimal Amount(JsonObject json, string name) => json[name] is JsonValue value && value.TryGetValue<decimal>(out var amount) ? amount : 0m;

    private static string? Text(JsonObject? json, string name) => json?[name] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text : null;

    private static JsonNode? Parse(string body)
    {
        try
        {
            return JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
