using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Domain.Personas;

/// <summary>What the seed file says of a Carrier Persona beyond who it is.</summary>
/// <param name="BookingReferencePrefix">The start of the booking references it confirms with, such as <c>VDT</c>.</param>
/// <param name="DefaultAnswerPolicy">How it answers until the presenter changes that.</param>
public sealed record CarrierProfile(string BookingReferencePrefix, CarrierAnswerPolicy DefaultAnswerPolicy);
