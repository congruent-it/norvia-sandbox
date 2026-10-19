using Norvia.PartnerPortal.Domain.CarrierInvoices;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.CarrierInvoices;

/// <summary>
/// The tracking contract's keys the portal knows of a Carrier invoice (coding guidelines §7), so its traces join Norvia's
/// Message trail by the correlation ID API Management gave the invoice and the Carrier's invoice number. Never its amounts.
/// </summary>
public static class CarrierInvoiceTracking
{
    public const string Flow = "carrier-invoice";

    /// <param name="correlationId">The ID Norvia gave the invoice; null when Norvia never replied, which leaves the key out.</param>
    public static Dictionary<string, object?> KeysOf(Persona persona, string invoiceNumber, string? correlationId)
    {
        ArgumentNullException.ThrowIfNull(persona);
        var keys = new Dictionary<string, object?>
        {
            ["flow"] = Flow,
            ["partyId"] = persona.PartyId,
            ["documentNumber"] = invoiceNumber,
        };
        if (correlationId is not null)
        {
            keys["correlationId"] = correlationId;
        }

        return keys;
    }

    /// <summary>The outcome of Norvia's reply, and its failure code when it has one, as the Logic App's run records it.</summary>
    public static Dictionary<string, object?> OutcomeOf(CarrierInvoiceReplyResult result) => result switch
    {
        CarrierInvoiceReplyResult.Received => new() { ["outcome"] = "Succeeded" },
        CarrierInvoiceReplyResult.InvoiceNumberUsed => new() { ["outcome"] = "Refused", ["failureCode"] = "InvoiceNumberUsed" },
        CarrierInvoiceReplyResult.CarrierNotFound => new() { ["outcome"] = "Refused", ["failureCode"] = "CarrierNotFound" },
        CarrierInvoiceReplyResult.Invalid => new() { ["outcome"] = "Refused", ["failureCode"] = "CarrierInvoiceUnreadable" },
        CarrierInvoiceReplyResult.Refused => new() { ["outcome"] = "Refused" },
        CarrierInvoiceReplyResult.NotReceived => new() { ["outcome"] = "Failed", ["failureCode"] = "CarrierInvoiceNotReceived" },
        _ => throw new ArgumentOutOfRangeException(nameof(result), result, "Every reply records its outcome."),
    };
}
