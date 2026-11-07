namespace Norvia.Erp.Application.CarrierInvoices.AgentView;

/// <summary>A likely candidate for an Unknown booking line. Never a certainty: the ERP ranks on how alike the references are, the amount and the delivery date.</summary>
/// <param name="Rank">1 for the best candidate.</param>
/// <param name="Difference">The invoiced amount minus the candidate's total.</param>
public sealed record AgentCandidateView(
    int Rank,
    string CarrierBookingNumber,
    string CarrierBookingReference,
    decimal Total,
    decimal Difference,
    string Source,
    DateOnly DeliveredOn);
