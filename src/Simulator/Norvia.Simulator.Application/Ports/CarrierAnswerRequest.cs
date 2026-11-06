namespace Norvia.Simulator.Application.Ports;

/// <summary>A Carrier's answer to the Carrier booking request it was sent: confirmed with its own booking reference, or declined with a reason code of the Carrier answer format and, for the reason <c>OTHER</c>, the reason in the Carrier's own words.</summary>
public sealed record CarrierAnswerRequest(string CarrierBookingNumber, bool Confirm, string? BookingReference, DateTimeOffset? PlannedCollection, string? DeclineReason, string? DeclineText = null);
