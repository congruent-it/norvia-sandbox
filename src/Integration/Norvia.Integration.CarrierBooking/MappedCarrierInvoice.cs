using System.Text.Json.Nodes;

namespace Norvia.Integration.CarrierBooking;

/// <summary>
/// What the Carrier invoice map made of a Carrier's invoice: the Canonical Model submission for the ERP billing API, or why the
/// invoice can't be used. The invoice number is there whenever the invoice named a usable one.
/// </summary>
public sealed record MappedCarrierInvoice
{
    private MappedCarrierInvoice()
    {
    }

    public string? InvoiceNumber { get; private init; }

    /// <summary>The body of the ERP call; null when the invoice can't be used.</summary>
    public JsonObject? ErpRequest { get; private init; }

    /// <summary>Why the invoice can't be used, in the Carrier's terms; null when it can.</summary>
    public string? Problem { get; private init; }

    public static MappedCarrierInvoice Mapped(string invoiceNumber, JsonObject erpRequest) =>
        new() { InvoiceNumber = invoiceNumber, ErpRequest = erpRequest };

    public static MappedCarrierInvoice Invalid(string? invoiceNumber, string problem) =>
        new() { InvoiceNumber = invoiceNumber, Problem = problem };

    /// <summary>As the Carrier invoice Logic App reads it: <c>invoiceNumber</c>, and <c>erpRequest</c> or <c>problem</c>.</summary>
    public string ToJsonString()
    {
        var mapped = new JsonObject();
        if (InvoiceNumber is not null)
        {
            mapped["invoiceNumber"] = InvoiceNumber;
        }

        if (ErpRequest is not null)
        {
            mapped["erpRequest"] = ErpRequest.DeepClone();
        }
        else
        {
            mapped["problem"] = Problem;
        }

        return mapped.ToJsonString();
    }

    /// <summary>Reads what <see cref="ToJsonString"/> wrote, as the relay gets it from the workflow.</summary>
    public static MappedCarrierInvoice FromJson(string json)
    {
        var mapped = JsonNode.Parse(json) as JsonObject ?? throw new ArgumentException("The mapped invoice isn't a JSON object.", nameof(json));
        var number = (mapped["invoiceNumber"] as JsonValue)?.GetValue<string>();
        return mapped["erpRequest"] is JsonObject request
            ? Mapped(number!, request)
            : Invalid(number, (mapped["problem"] as JsonValue)?.GetValue<string>() ?? "The invoice can't be used.");
    }
}
