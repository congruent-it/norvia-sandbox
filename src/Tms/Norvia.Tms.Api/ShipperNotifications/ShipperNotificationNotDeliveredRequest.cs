namespace Norvia.Tms.Api.ShipperNotifications;

/// <summary>The Shipper notification Logic App's report that a notification never reached the Shipper.</summary>
/// <param name="StatusCode">The Shipper endpoint's HTTP status code. Optional: a timeout has none.</param>
/// <param name="Message">The technical cause in plain language. Required.</param>
public sealed record ShipperNotificationNotDeliveredRequest(int? StatusCode, string? Message);
