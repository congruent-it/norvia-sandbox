namespace Norvia.Simulator.Application.Ports;

/// <summary>A Carrier's invoice as it sends it: its own invoice number, the date and one line per Carrier booking it claims, by its own booking reference.</summary>
public sealed record CarrierInvoiceRequest(string InvoiceNumber, DateOnly InvoiceDate, IReadOnlyList<CarrierInvoiceLineRequest> Lines);

/// <param name="BookingReference">The Carrier's own booking reference, as it confirmed with.</param>
/// <param name="Amount">What the Carrier claims, in EUR without VAT.</param>
public sealed record CarrierInvoiceLineRequest(string BookingReference, decimal Amount, string? Description);
