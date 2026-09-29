namespace Norvia.Tms.Api.CarrierBookings;

/// <summary>The P4 booking Logic App's report that a Requested Carrier booking's request never reached the Carrier.</summary>
/// <param name="StatusCode">The Carrier endpoint's HTTP status code, or the technical failure's own code. Required.</param>
/// <param name="Message">The technical cause in plain language. Required.</param>
public sealed record CarrierBookingNotDeliveredRequest(int? StatusCode, string? Message);
