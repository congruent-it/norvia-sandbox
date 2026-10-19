namespace Norvia.PartnerPortal.Application.CarrierInvoices.Draft;

/// <summary>The draft of the Carrier invoice a Carrier Persona may send now: what it has delivered and not invoiced yet.</summary>
/// <param name="SuggestedInvoiceNumber">A number the Persona hasn't used; the presenter may type another.</param>
public sealed record CarrierInvoiceDraftView(string SuggestedInvoiceNumber, DateOnly InvoiceDate, IReadOnlyList<CarrierInvoiceDraftLine> Lines);

/// <param name="BookingRef">The Carrier's own booking reference.</param>
/// <param name="AgreedAmount">The price from the booking request in EUR; null for a request that carried none.</param>
public sealed record CarrierInvoiceDraftLine(string BookingRef, string CarrierBookingNumber, string ShipmentNumber, decimal? AgreedAmount);
