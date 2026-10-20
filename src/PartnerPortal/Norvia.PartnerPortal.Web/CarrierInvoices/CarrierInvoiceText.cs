using Microsoft.FluentUI.AspNetCore.Components;
using Norvia.PartnerPortal.Application.CarrierInvoices.Submit;
using Norvia.PartnerPortal.Domain.CarrierInvoices;
using Norvia.PartnerPortal.Web.ShipperInvoices;

namespace Norvia.PartnerPortal.Web.CarrierInvoices;

/// <summary>The words the Carrier invoice screens use.</summary>
public static class CarrierInvoiceText
{
    /// <summary>What Norvia made of a line when it received the invoice.</summary>
    public static string Of(CarrierInvoiceLineResult result) => result switch
    {
        CarrierInvoiceLineResult.Matched => "Matched",
        CarrierInvoiceLineResult.PriceMismatch => "Price mismatch",
        CarrierInvoiceLineResult.UnknownBooking => "Unknown booking",
        CarrierInvoiceLineResult.Duplicate => "Duplicate",
        _ => throw new ArgumentOutOfRangeException(nameof(result), result, "The screens have no words for this result."),
    };

    /// <summary>What Norvia made of a line on receipt, or a dash for an invoice the portal never saw.</summary>
    public static string OnReceipt(CarrierInvoiceLineResult? result) => result is { } known ? Of(known) : "—";

    /// <summary>What a line or an invoice claims, or a dash for an invoice the portal never saw.</summary>
    public static string Claimed(decimal? amount) => amount is { } known ? InvoiceText.Money(known) : "—";

    /// <summary>What became of a line once Norvia told the outcome.</summary>
    public static string Of(CarrierInvoiceOutcomeResult result) => result switch
    {
        CarrierInvoiceOutcomeResult.Matched => "Matched",
        CarrierInvoiceOutcomeResult.Accepted => "Accepted",
        CarrierInvoiceOutcomeResult.AcceptedAdjusted => "Accepted, adjusted",
        CarrierInvoiceOutcomeResult.Rejected => "Rejected",
        _ => throw new ArgumentOutOfRangeException(nameof(result), result, "The screens have no words for this result."),
    };

    public static string Of(CarrierInvoiceStatus status) => status switch
    {
        CarrierInvoiceStatus.Received => "Received",
        CarrierInvoiceStatus.Approved => "Approved",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "The screens have no words for this status."),
    };

    /// <summary>The outcome of a line as the invoices page shows it: what Norvia told, with the amount it accepted, or "Waiting" until it does.</summary>
    public static string Outcome(CarrierInvoiceOutcomeLine? line) => line switch
    {
        null => "Waiting for Norvia",
        { Result: CarrierInvoiceOutcomeResult.Rejected } => Of(line.Result),
        _ => $"{Of(line.Result)} at {InvoiceText.Money(line.AcceptedAmount)}",
    };

    /// <summary>The text of an amount in a field: two decimals, written the same on every host.</summary>
    public static string Amount(decimal? amount) => amount is { } value ? value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) : string.Empty;

    /// <summary>The amount a field says, or false for any other text.</summary>
    public static bool TryParseAmount(string? text, out decimal amount) =>
        decimal.TryParse(text?.Trim(), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out amount);

    public static CarrierInvoiceNotice NoticeOf(SubmittedCarrierInvoice sent)
    {
        ArgumentNullException.ThrowIfNull(sent);
        var reply = sent.Reply;
        var what = $"invoice {sent.InvoiceNumber}";
        return reply.Result switch
        {
            CarrierInvoiceReplyResult.Received when reply.Receipt!.Status == CarrierInvoiceStatus.Approved =>
                new($"Norvia received {what} and approved it at {InvoiceText.Money(reply.Receipt.ApprovedAmount ?? 0)}: every line matched.", MessageIntent.Success, reply.MessageId),
            CarrierInvoiceReplyResult.Received =>
                new($"Norvia received {what}. A line waits for Norvia's Finance, which tells you the outcome later.", MessageIntent.Info, reply.MessageId),
            CarrierInvoiceReplyResult.InvoiceNumberUsed =>
                new(reply.Text ?? $"Norvia has {what} with different content. Send the claim with a new invoice number.", MessageIntent.Error, reply.MessageId),
            CarrierInvoiceReplyResult.NotReceived =>
                new($"Norvia did not receive {what}. Send it again in a moment.", MessageIntent.Warning, reply.MessageId),
            _ => new(reply.Text ?? $"Norvia refused {what}.", MessageIntent.Error, reply.MessageId),
        };
    }
}

/// <param name="MessageId">The ID Norvia gave the invoice, for asking Norvia about it; null when Norvia never replied.</param>
public sealed record CarrierInvoiceNotice(string Text, MessageIntent Intent, string? MessageId);
