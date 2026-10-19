namespace Norvia.PartnerPortal.Domain.CarrierInvoices;

/// <param name="BookingRef">The Carrier's own booking reference of the line.</param>
/// <param name="AcceptedAmount">What Norvia accepts for the line, in EUR; zero for a rejected line.</param>
/// <param name="Remark">Finance's remark on an adjusted amount, or its reason for a rejection; null otherwise.</param>
public sealed record CarrierInvoiceOutcomeLine(string BookingRef, CarrierInvoiceOutcomeResult Result, decimal AcceptedAmount, string? Remark);
