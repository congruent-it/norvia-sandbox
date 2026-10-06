namespace Norvia.Tms.Api.Exceptions;

/// <summary>The Planner's message to the Shipper, starting from the template for the Exception's type, and who sent it.</summary>
/// <param name="Message">Required: what the Shipper is told.</param>
/// <param name="Planner">Required: the Planner's name, as the TMS UI names them.</param>
public sealed record NotifyShipperRequest(string? Message, string? Planner);
