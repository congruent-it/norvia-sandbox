namespace Norvia.PartnerPortal.Domain.CarrierInvoices;

/// <summary>One Carrier booking a Carrier invoice claims, with what Norvia made of it when it received the invoice.</summary>
/// <param name="BookingRef">The Carrier's own booking reference, as it gave it when it confirmed.</param>
/// <param name="Amount">What the Carrier claims, in EUR without VAT.</param>
public sealed record CarrierInvoiceLine(string BookingRef, decimal Amount, string? Description, CarrierInvoiceLineResult Result);
