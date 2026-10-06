namespace Norvia.Tms.Api.CarrierBookings;

/// <summary>The Logic App's report that a Carrier booking's message never reached the Carrier: the P4 booking request, or the P7 cancellation or withdrawal.</summary>
/// <param name="StatusCode">The Carrier endpoint's HTTP status code, or the technical failure's own code. Required.</param>
/// <param name="Message">The technical cause in plain language. Required.</param>
public sealed record CarrierBookingNotDeliveredRequest(int? StatusCode, string? Message);
